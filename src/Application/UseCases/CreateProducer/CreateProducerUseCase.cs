using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateProducer.Inputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.BoundedContexts.ProducerContext.Events;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Application.UseCases.CreateProducer;

public sealed class CreateProducerUseCase : IUseCase<CreateProducerUseCaseInput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PasswordConfiguration _passwordConfiguration;
    private readonly CreateProducerDiagnostics _diagnostics;
    private readonly ILogger<CreateProducerUseCase> _logger;

    public CreateProducerUseCase(
        IProducerRepository producerRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        PasswordConfiguration passwordConfiguration,
        CreateProducerDiagnostics diagnostics,
        ILogger<CreateProducerUseCase> logger)
    {
        _producerRepository = producerRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _passwordConfiguration = passwordConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string CREATE_PRODUCER_SUCCESS_NOTIFICATION_CODE = "CREATE_PRODUCER_SUCCESS";
    private const string CREATE_PRODUCER_SUCCESS_NOTIFICATION_MESSAGE = "A produtora foi cadastrada. Verifique o e-mail informado para acessar a plataforma.";

    public const string CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE = "CREATE_PRODUCER_CANCELED";
    private const string CREATE_PRODUCER_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de cadastro foi cancelada.";

    private const string PRODUCER_EMAIL_ALREADY_EXISTS_NOTIFICATION_CODE = "PRODUCER_EMAIL_ALREADY_EXISTS";
    private const string PRODUCER_EMAIL_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe um cadastro de produtora com o e-mail informado.";

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

            var baseSlug = SlugValueObject.Build(input.Name.Value, SlugValueObject.SUBDOMAIN_MAX_LENGTH);

            if (!baseSlug.IsValid)
                return BuildInvalidDataResult(baseSlug.MethodResult);

            // Antes da transação: o hash é lento de propósito e não pode segurar locks no banco.
            var passwordSalt = GeneratePasswordSalt();
            var passwordHash = HashPassword(input.AdministratorPassword, passwordSalt);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            if (await _producerRepository.ExistsByEmailAsync(input.Email.Value, cancellationToken))
                return await RejectEmailAlreadyExistsAsync(input);

            var producer = BuildProducer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug, cancellationToken));
            var producerCreationResult = await _producerRepository.CreateAsync(BuildProducerModel(producer), cancellationToken);

            while (producerCreationResult == ProducerCreationResult.SlugAlreadyExists)
            {
                CreateProducerLog.SlugTaken(_logger, input.Email.GetObfuscatedValue());

                producer = BuildProducer(input, auditableInfo, await GetAvailableSlugAsync(baseSlug.BuildRandomVersion(), cancellationToken));
                producerCreationResult = await _producerRepository.CreateAsync(BuildProducerModel(producer), cancellationToken);
            }

            if (producerCreationResult == ProducerCreationResult.EmailAlreadyExists)
                return await RejectEmailAlreadyExistsAsync(input);

            var administrator = BuildAdministrator(input, auditableInfo, producer, passwordHash, passwordSalt);
            var administratorCreationResult = await _producerRepository.CreateUserAsync(BuildProducerUserModel(administrator), cancellationToken);

            // Não existe produtora sem administrador: a recusa desfaz também a produtora.
            if (administratorCreationResult == ProducerUserCreationResult.EmailAlreadyExists)
                return await RejectEmailAlreadyExistsAsync(input);

            await _outboxRepository.AddAsync(BuildProducerRegisteredOutboxEvent(producer), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            return BuildSuccessResult(producer, administrator);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            CreateProducerLog.Failed(_logger, ex);

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

    // Palavra reservada é tratada como slug ocupado: recebe a versão aleatória.
    private async Task<SlugValueObject> GetAvailableSlugAsync(SlugValueObject slug, CancellationToken cancellationToken)
    {
        while (slug.IsReservedSubdomain || await _producerRepository.ExistsBySlugAsync(slug.Value, cancellationToken))
            slug = slug.BuildRandomVersion();

        return slug;
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            CreateProducerLog.RollbackFailed(_logger, ex);
        }
    }

    private static Producer BuildProducer(CreateProducerUseCaseInput input, AuditableInfoValueObject auditableInfo, SlugValueObject slug)
        => new(
            id: IdValueObject.Build(),
            name: input.Name,
            email: input.Email,
            phone: input.Phone,
            slug: slug,
            status: ProducerStatus.PENDING,
            createdAt: DateTimeValueObject.Build(DateTime.UtcNow),
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static ProducerUser BuildAdministrator(CreateProducerUseCaseInput input, AuditableInfoValueObject auditableInfo, Producer producer, PasswordHashValueObject passwordHash, PasswordSaltValueObject passwordSalt)
        => new(
            id: IdValueObject.Build(),
            producerId: producer.Id,
            name: input.AdministratorName,
            email: input.Email,
            passwordHash: passwordHash,
            passwordSalt: passwordSalt,
            userType: ProducerUserType.ADMINISTRATOR,
            status: ProducerUserStatus.PENDING,
            createdAt: producer.CreatedAt,
            createdCorrelationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

    private static ProducerModel BuildProducerModel(Producer producer)
        => new(
            Id: producer.Id.Value,
            Name: producer.Name.Value,
            Email: producer.Email.Value,
            Phone: producer.Phone.Value,
            Slug: producer.Slug.Value,
            Status: producer.Status.ToString(),
            CreatedAt: producer.CreatedAt.Value,
            CreatedCorrelationId: producer.CreatedCorrelationId.Value);

    private static ProducerUserModel BuildProducerUserModel(ProducerUser producerUser)
        => new(
            Id: producerUser.Id.Value,
            ProducerId: producerUser.ProducerId.Value,
            Name: producerUser.Name.Value,
            Email: producerUser.Email.Value,
            PasswordHash: producerUser.PasswordHash.Value,
            PasswordSalt: producerUser.PasswordSalt.Value,
            UserType: producerUser.UserType.ToString(),
            Status: producerUser.Status.ToString(),
            CreatedAt: producerUser.CreatedAt.Value,
            CreatedCorrelationId: producerUser.CreatedCorrelationId.Value);

    private static OutboxEventModel BuildProducerRegisteredOutboxEvent(Producer producer)
    {
        var producerRegistered = new ProducerRegisteredEvent(ProducerId: producer.Id.Value, OccurredAt: producer.CreatedAt.Value);

        return new OutboxEventModel(
            Id: Guid.NewGuid(),
            EventType: ProducerRegisteredEvent.EVENT_TYPE,
            AggregateId: producer.Id.Value,
            Payload: JsonSerializer.Serialize(producerRegistered),
            CreatedAt: producer.CreatedAt.Value,
            CreatedCorrelationId: producer.CreatedCorrelationId.Value);
    }

    private MethodResult BuildSuccessResult(Producer producer, ProducerUser administrator)
    {
        RecordOutcome(CreateProducerDiagnostics.OUTCOME_CREATED);

        Activity.Current?.SetTag("ntix.producer_id", producer.Id.Value);

        CreateProducerLog.Created(
            logger: _logger,
            producerId: producer.Id.Value,
            producerUserId: administrator.Id.Value,
            obfuscatedEmail: producer.Email.GetObfuscatedValue());

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
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return validationResult;
    }

    private async Task<MethodResult> RejectEmailAlreadyExistsAsync(CreateProducerUseCaseInput input)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(CreateProducerDiagnostics.OUTCOME_ALREADY_EXISTS);

        CreateProducerLog.AlreadyExists(
            logger: _logger,
            obfuscatedEmail: input.Email.GetObfuscatedValue());

        var alreadyExistsNotification = Notification.Error(
            code: PRODUCER_EMAIL_ALREADY_EXISTS_NOTIFICATION_CODE,
            message: PRODUCER_EMAIL_ALREADY_EXISTS_NOTIFICATION_MESSAGE);

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
