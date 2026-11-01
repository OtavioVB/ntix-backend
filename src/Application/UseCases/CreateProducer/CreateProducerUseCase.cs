using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateProducer.Inputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Resend.Base.EmailSender.Interfaces;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Infra.SqlServer.Repositories.Producers.Enums;
using Infra.SqlServer.Repositories.Producers.Interfaces;
using Infra.SqlServer.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace Application.UseCases.CreateProducer;

public sealed class CreateProducerUseCase : IUseCase<CreateProducerUseCaseInput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly PasswordConfiguration _passwordConfiguration;
    private readonly EmailVerificationConfiguration _emailVerificationConfiguration;
    private readonly CreateProducerDiagnostics _diagnostics;
    private readonly ILogger<CreateProducerUseCase> _logger;

    public CreateProducerUseCase(
        IProducerRepository producerRepository,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        PasswordConfiguration passwordConfiguration,
        EmailVerificationConfiguration emailVerificationConfiguration,
        CreateProducerDiagnostics diagnostics,
        ILogger<CreateProducerUseCase> logger)
    {
        _producerRepository = producerRepository;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _passwordConfiguration = passwordConfiguration;
        _emailVerificationConfiguration = emailVerificationConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string CREATE_PRODUCER_SUCCESS_NOTIFICATION_CODE = "CREATE_PRODUCER_SUCCESS";
    private const string CREATE_PRODUCER_SUCCESS_NOTIFICATION_MESSAGE = "A produtora foi cadastrada com sucesso. Verifique o email para acessar a plataforma.";

    public const string CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE = "CREATE_PRODUCER_CANCELED";
    private const string CREATE_PRODUCER_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de cadastro foi cancelada.";

    private const string PRODUCER_ALREADY_EXISTS_NOTIFICATION_CODE = "PRODUCER_ALREADY_EXISTS";
    private const string PRODUCER_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe uma produtora ou um usuário com o email informado.";

    private const string PRODUCER_DOCUMENT_ALREADY_EXISTS_NOTIFICATION_CODE = "PRODUCER_DOCUMENT_ALREADY_EXISTS";
    private const string PRODUCER_DOCUMENT_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe uma produtora com o documento informado.";

    private const string VERIFICATION_EMAIL_SUBJECT = "Confirme seu email na Ntix";

    public async Task<MethodResult> ExecuteAsync(CreateProducerUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = StartActivity(auditableInfo);

        _diagnostics.RegistrationRequested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var baseSlug = SlugValueObject.BuildSubdomain(input.Name.Value);

            if (!baseSlug.IsValid)
                return BuildInvalidDataResult(baseSlug.MethodResult);

            // Antes da transação: o hash é lento de propósito e não pode segurar locks no banco.
            var passwordSalt = GeneratePasswordSalt();
            var passwordHash = HashPassword(input.Password, passwordSalt);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var emailExists = await _producerRepository.ExistsByEmailAsync(input.Email.Value, cancellationToken);

            if (emailExists)
                return await RejectProducerAlreadyExistsAsync(input);

            var documentExists = input.Document.Value is not null
                && await _producerRepository.ExistsByDocumentAsync(input.Document.Value, cancellationToken);

            if (documentExists)
                return await RejectDocumentAlreadyExistsAsync(input);

            var producer = BuildProducer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug, cancellationToken));
            var creationResult = await _producerRepository.CreateAsync(BuildProducerModel(producer), cancellationToken);

            while (creationResult == ProducerCreationResult.SlugAlreadyExists)
            {
                CreateProducerLog.SlugTaken(
                    logger: _logger,
                    methodName: nameof(CreateProducerUseCase),
                    input: new { Email = input.Email.GetObfuscatedValue() });

                producer = BuildProducer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug.BuildRandomVersion(), cancellationToken));
                creationResult = await _producerRepository.CreateAsync(BuildProducerModel(producer), cancellationToken);
            }

            if (creationResult == ProducerCreationResult.EmailAlreadyExists)
                return await RejectProducerAlreadyExistsAsync(input);

            if (creationResult == ProducerCreationResult.DocumentAlreadyExists)
                return await RejectDocumentAlreadyExistsAsync(input);

            var user = BuildAdministrator(input, auditableInfo, producer, passwordHash, passwordSalt);
            var userCreationResult = await _producerRepository.CreateUserAsync(BuildUserModel(user), cancellationToken);

            if (userCreationResult == ProducerUserCreationResult.EmailAlreadyExists)
                return await RejectProducerAlreadyExistsAsync(input);

            var emailVerification = BuildEmailVerification(auditableInfo, user);

            await _producerRepository.CreateEmailVerificationAsync(BuildEmailVerificationModel(emailVerification), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            await SendVerificationEmailAsync(producer, user, emailVerification);

            return BuildSuccessResult(producer, user);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            CreateProducerLog.Failed(
                logger: _logger,
                methodName: nameof(CreateProducerUseCase),
                input: new { ExceptionType = ex.GetType().Name },
                exception: ex);

            // Só o tipo: a mensagem de uma exceção de banco pode trazer dados pessoais.
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateProducerDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private static PasswordSaltValueObject GeneratePasswordSalt()
    {
        using var activity = CreateProducerDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateProducer.GeneratePasswordSalt");

        return PasswordSaltValueObject.Build();
    }

    private PasswordHashValueObject HashPassword(PasswordValueObject password, PasswordSaltValueObject salt)
    {
        using var activity = CreateProducerDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateProducer.HashPassword");

        activity?.SetTag("ntix.password.iterations", PasswordHashValueObject.ITERATIONS);

        var startedAt = Stopwatch.GetTimestamp();
        var hash = PasswordHashValueObject.Build(password, salt, _passwordConfiguration.PrivateKey);

        _diagnostics.PasswordHashed(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        return hash;
    }

    private static Activity? StartActivity(AuditableInfoValueObject auditableInfo)
    {
        var activity = CreateProducerDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateProducer");

        if (activity is not null && auditableInfo.IsValid)
        {
            activity.SetTag("ntix.correlation_id", auditableInfo.CorrelationId);
            activity.SetTag("ntix.origin_service", auditableInfo.OriginService);
            activity.SetTag("ntix.execution_user", auditableInfo.ExecutionUser);
            activity.SetTag("ntix.requested_at", auditableInfo.RequestedAt);
        }

        return activity;
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.RegistrationFinished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private async Task<SlugValueObject> GetAvailableSlugAsync(SlugValueObject slug, CancellationToken cancellationToken)
    {
        while (await _producerRepository.ExistsBySlugAsync(slug.Value, cancellationToken))
            slug = slug.BuildRandomVersion();

        return slug;
    }

    // Depois do commit: a falha no envio não desfaz o cadastro, e o cancelamento da requisição não interrompe o envio.
    private async Task SendVerificationEmailAsync(Producer producer, ProducerUser user, EmailVerification emailVerification)
    {
        using var activity = CreateProducerDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateProducer.SendVerificationEmail");

        try
        {
            await _emailSender.SendAsync(
                to: user.Email.Value,
                subject: VERIFICATION_EMAIL_SUBJECT,
                html: BuildVerificationEmailHtml(user, emailVerification),
                cancellationToken: CancellationToken.None);

            _diagnostics.VerificationEmailFinished(CreateProducerDiagnostics.VERIFICATION_EMAIL_SENT);

            CreateProducerLog.VerificationEmailSent(
                logger: _logger,
                methodName: nameof(CreateProducerUseCase),
                input: new { ProducerId = producer.Id.Value, ProducerUserId = user.Id.Value, Email = user.Email.GetObfuscatedValue() });
        }
        catch (Exception ex)
        {
            _diagnostics.VerificationEmailFinished(CreateProducerDiagnostics.VERIFICATION_EMAIL_FAILED);

            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            CreateProducerLog.VerificationEmailFailed(
                logger: _logger,
                methodName: nameof(CreateProducerUseCase),
                input: new { ProducerId = producer.Id.Value, ProducerUserId = user.Id.Value, ExceptionType = ex.GetType().Name },
                exception: ex);
        }
    }

    private string BuildVerificationEmailHtml(ProducerUser user, EmailVerification emailVerification)
    {
        var link = $"{_emailVerificationConfiguration.LinkBaseUrl}?token={emailVerification.Token.Value}";

        return $"""
            <p>Olá, {WebUtility.HtmlEncode(user.Name.Value)}!</p>
            <p>Confirme seu email para acessar a Ntix. O link é válido por {EmailVerification.VALIDITY.TotalHours:0} horas.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Confirmar email</a></p>
            """;
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            CreateProducerLog.RollbackFailed(
                logger: _logger,
                methodName: nameof(CreateProducerUseCase),
                input: new { ExceptionType = ex.GetType().Name },
                exception: ex);
        }
    }

    private static Producer BuildProducer(CreateProducerUseCaseInput input, AuditableInfoValueObject auditableInfo, SlugValueObject slug)
        => new(
            id: IdValueObject.Build(),
            name: input.Name,
            email: input.Email,
            phone: input.Phone,
            document: input.Document,
            slug: slug,
            createdAt: DateTimeValueObject.Build(DateTime.UtcNow),
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static ProducerUser BuildAdministrator(CreateProducerUseCaseInput input, AuditableInfoValueObject auditableInfo, Producer producer, PasswordHashValueObject passwordHash, PasswordSaltValueObject passwordSalt)
        => new(
            id: IdValueObject.Build(),
            producerId: producer.Id,
            name: input.UserName,
            email: input.Email,
            passwordHash: passwordHash,
            passwordSalt: passwordSalt,
            userType: ProducerUserType.ADMINISTRATOR,
            createdAt: DateTimeValueObject.Build(DateTime.UtcNow),
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static EmailVerification BuildEmailVerification(AuditableInfoValueObject auditableInfo, ProducerUser user)
        => new(
            id: IdValueObject.Build(),
            producerUserId: user.Id,
            token: EmailVerificationTokenValueObject.Build(),
            createdAt: DateTimeValueObject.Build(DateTime.UtcNow),
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static ProducerModel BuildProducerModel(Producer producer)
        => new(
            Id: producer.Id.Value,
            Name: producer.Name.Value,
            Email: producer.Email.Value,
            Phone: producer.Phone.Value,
            Document: producer.Document.Value,
            Slug: producer.Slug.Value,
            Status: producer.Status.ToString(),
            CreatedAt: producer.CreatedAt.Value,
            CreatedCorrelationId: producer.CreatedCorrelationId.Value);

    private static ProducerUserModel BuildUserModel(ProducerUser user)
        => new(
            Id: user.Id.Value,
            ProducerId: user.ProducerId.Value,
            Name: user.Name.Value,
            Email: user.Email.Value,
            PasswordHash: user.PasswordHash.Value,
            PasswordSalt: user.PasswordSalt.Value,
            UserType: user.UserType.ToString(),
            Status: user.Status.ToString(),
            CreatedAt: user.CreatedAt.Value,
            CreatedCorrelationId: user.CreatedCorrelationId.Value);

    private static EmailVerificationModel BuildEmailVerificationModel(EmailVerification emailVerification)
        => new(
            Id: emailVerification.Id.Value,
            ProducerUserId: emailVerification.ProducerUserId.Value,
            TokenHash: emailVerification.Token.GetHash(),
            ExpiresAt: emailVerification.ExpiresAt.Value,
            CreatedAt: emailVerification.CreatedAt.Value,
            CreatedCorrelationId: emailVerification.CreatedCorrelationId.Value);

    private MethodResult BuildSuccessResult(Producer producer, ProducerUser user)
    {
        RecordOutcome(CreateProducerDiagnostics.OUTCOME_CREATED);

        Activity.Current?.SetTag("ntix.producer_id", producer.Id.Value);

        CreateProducerLog.Created(
            logger: _logger,
            methodName: nameof(CreateProducerUseCase),
            input: new
            {
                ProducerId = producer.Id.Value,
                ProducerUserId = user.Id.Value,
                Email = producer.Email.GetObfuscatedValue(),
            });

        var successNotification = Notification.Success(
            code: CREATE_PRODUCER_SUCCESS_NOTIFICATION_CODE,
            message: CREATE_PRODUCER_SUCCESS_NOTIFICATION_MESSAGE);

        return MethodResult.Success([successNotification]);
    }

    private MethodResult BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(CreateProducerDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateProducerLog.InvalidData(
            logger: _logger,
            methodName: nameof(CreateProducerUseCase),
            input: new { NotificationCodes = string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)) });

        return validationResult;
    }

    private async Task<MethodResult> RejectProducerAlreadyExistsAsync(CreateProducerUseCaseInput input)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateProducerDiagnostics.OUTCOME_ALREADY_EXISTS);

        CreateProducerLog.AlreadyExists(
            logger: _logger,
            methodName: nameof(CreateProducerUseCase),
            input: new { Email = input.Email.GetObfuscatedValue() });

        var alreadyExistsNotification = Notification.Error(
            code: PRODUCER_ALREADY_EXISTS_NOTIFICATION_CODE,
            message: PRODUCER_ALREADY_EXISTS_NOTIFICATION_MESSAGE);

        return MethodResult.Error([alreadyExistsNotification]);
    }

    private async Task<MethodResult> RejectDocumentAlreadyExistsAsync(CreateProducerUseCaseInput input)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateProducerDiagnostics.OUTCOME_ALREADY_EXISTS);

        CreateProducerLog.DocumentAlreadyExists(
            logger: _logger,
            methodName: nameof(CreateProducerUseCase),
            input: new { Email = input.Email.GetObfuscatedValue() });

        var alreadyExistsNotification = Notification.Error(
            code: PRODUCER_DOCUMENT_ALREADY_EXISTS_NOTIFICATION_CODE,
            message: PRODUCER_DOCUMENT_ALREADY_EXISTS_NOTIFICATION_MESSAGE);

        return MethodResult.Error([alreadyExistsNotification]);
    }

    private static MethodResult BuildCanceledResult()
    {
        var canceledNotification = Notification.Error(
            code: CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE,
            message: CREATE_PRODUCER_CANCELED_NOTIFICATION_MESSAGE);

        return MethodResult.Error([canceledNotification]);
    }
}
