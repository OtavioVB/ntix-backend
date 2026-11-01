using Application.UseCases.AuthenticateCustomer.Inputs;
using Application.UseCases.AuthenticateCustomer.Outputs;
using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Jwt.Base.TokenGenerator.Interfaces;
using Infra.SqlServer.Repositories.Customers.Interfaces;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.AuthenticateCustomer;

public sealed class AuthenticateCustomerUseCase : IUseCase<AuthenticateCustomerUseCaseInput, AuthenticateCustomerUseCaseOutput>
{
    private const int TOKEN_LIFETIME_HOURS = 4;

    private readonly ICustomerRepository _customerRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly PasswordConfiguration _passwordConfiguration;
    private readonly AuthenticateCustomerDiagnostics _diagnostics;
    private readonly ILogger<AuthenticateCustomerUseCase> _logger;

    public AuthenticateCustomerUseCase(
        ICustomerRepository customerRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        PasswordConfiguration passwordConfiguration,
        AuthenticateCustomerDiagnostics diagnostics,
        ILogger<AuthenticateCustomerUseCase> logger)
    {
        _customerRepository = customerRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _passwordConfiguration = passwordConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string AUTHENTICATE_CUSTOMER_SUCCESS_NOTIFICATION_CODE = "AUTHENTICATE_CUSTOMER_SUCCESS";
    private const string AUTHENTICATE_CUSTOMER_SUCCESS_NOTIFICATION_MESSAGE = "O cliente foi autenticado com sucesso.";

    public const string AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_CODE = "AUTHENTICATE_CUSTOMER_CANCELED";
    private const string AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de autenticação foi cancelada.";

    public const string AUTHENTICATION_FAILED_NOTIFICATION_CODE = "AUTHENTICATION_FAILED";
    private const string AUTHENTICATION_FAILED_NOTIFICATION_MESSAGE = "Não foi possível autenticar com os dados informados.";

    public async Task<MethodResult<AuthenticateCustomerUseCaseOutput>> ExecuteAsync(AuthenticateCustomerUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = StartActivity(auditableInfo);

        _diagnostics.AuthenticationRequested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var credentials = await FindCredentialsAsync(input, cancellationToken);

            if (credentials is null)
            {
                // O hash também roda aqui para o tempo da resposta não revelar se o login existe.
                HashPassword(input.Password, PasswordSaltValueObject.Build());

                return RejectCustomerNotFound(input);
            }

            var passwordHash = HashPassword(input.Password, PasswordSaltValueObject.Build(credentials.PasswordSalt));

            if (!passwordHash.Matches(credentials.PasswordHash))
                return RejectInvalidPassword(credentials);

            if (!credentials.IsLinkedToProducer)
                return RejectNotLinkedToProducer(credentials, input);

            return BuildSuccessResult(credentials, input);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            AuthenticateCustomerLog.Failed(
                logger: _logger,
                methodName: nameof(AuthenticateCustomerUseCase),
                input: new { ExceptionType = ex.GetType().Name },
                exception: ex);

            // Só o tipo: a mensagem de uma exceção de banco pode trazer dados pessoais.
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private Task<CustomerCredentialsModel?> FindCredentialsAsync(AuthenticateCustomerUseCaseInput input, CancellationToken cancellationToken)
        => input.Email is not null
            ? _customerRepository.FindCredentialsByEmailAsync(input.Email.Value.Value, input.ProducerId.Value, cancellationToken)
            : _customerRepository.FindCredentialsByDocumentAsync(input.Document!.Value.Value, input.ProducerId.Value, cancellationToken);

    private PasswordHashValueObject HashPassword(PasswordValueObject password, PasswordSaltValueObject salt)
    {
        using var activity = AuthenticateCustomerDiagnostics.ACTIVITY_SOURCE.StartActivity("AuthenticateCustomer.HashPassword");

        activity?.SetTag("ntix.password.iterations", PasswordHashValueObject.ITERATIONS);

        return PasswordHashValueObject.Build(password, salt, _passwordConfiguration.PrivateKey);
    }

    private static Activity? StartActivity(AuditableInfoValueObject auditableInfo)
    {
        var activity = AuthenticateCustomerDiagnostics.ACTIVITY_SOURCE.StartActivity("AuthenticateCustomer");

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
        _diagnostics.AuthenticationFinished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private MethodResult<AuthenticateCustomerUseCaseOutput> BuildSuccessResult(CustomerCredentialsModel credentials, AuthenticateCustomerUseCaseInput input)
    {
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddHours(TOKEN_LIFETIME_HOURS);

        var accessToken = _jwtTokenGenerator.Generate(
            customerId: credentials.Id,
            producerId: input.ProducerId.Value,
            issuedAt: issuedAt,
            expiresAt: expiresAt);

        RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_AUTHENTICATED);

        Activity.Current?.SetTag("ntix.customer_id", credentials.Id);

        AuthenticateCustomerLog.Authenticated(
            logger: _logger,
            methodName: nameof(AuthenticateCustomerUseCase),
            input: new
            {
                CustomerId = credentials.Id,
                ProducerId = input.ProducerId.Value,
            });

        var successNotification = Notification.Success(
            code: AUTHENTICATE_CUSTOMER_SUCCESS_NOTIFICATION_CODE,
            message: AUTHENTICATE_CUSTOMER_SUCCESS_NOTIFICATION_MESSAGE);

        return MethodResult<AuthenticateCustomerUseCaseOutput>.Success(
            output: new AuthenticateCustomerUseCaseOutput(accessToken, expiresAt),
            notifications: [successNotification]);
    }

    private MethodResult<AuthenticateCustomerUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_VALIDATION_FAILED);

        AuthenticateCustomerLog.InvalidData(
            logger: _logger,
            methodName: nameof(AuthenticateCustomerUseCase),
            input: new { NotificationCodes = string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)) });

        return MethodResult<AuthenticateCustomerUseCaseOutput>.Error(validationResult.Notifications);
    }

    private MethodResult<AuthenticateCustomerUseCaseOutput> RejectCustomerNotFound(AuthenticateCustomerUseCaseInput input)
    {
        RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_CUSTOMER_NOT_FOUND);

        AuthenticateCustomerLog.CustomerNotFound(
            logger: _logger,
            methodName: nameof(AuthenticateCustomerUseCase),
            input: new
            {
                Login = input.GetObfuscatedLogin(),
                ProducerId = input.ProducerId.Value,
            });

        return BuildAuthenticationFailedResult();
    }

    private MethodResult<AuthenticateCustomerUseCaseOutput> RejectInvalidPassword(CustomerCredentialsModel credentials)
    {
        RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_INVALID_PASSWORD);

        AuthenticateCustomerLog.InvalidPassword(
            logger: _logger,
            methodName: nameof(AuthenticateCustomerUseCase),
            input: new { CustomerId = credentials.Id });

        return BuildAuthenticationFailedResult();
    }

    private MethodResult<AuthenticateCustomerUseCaseOutput> RejectNotLinkedToProducer(CustomerCredentialsModel credentials, AuthenticateCustomerUseCaseInput input)
    {
        RecordOutcome(AuthenticateCustomerDiagnostics.OUTCOME_NOT_LINKED_TO_PRODUCER);

        AuthenticateCustomerLog.NotLinkedToProducer(
            logger: _logger,
            methodName: nameof(AuthenticateCustomerUseCase),
            input: new
            {
                CustomerId = credentials.Id,
                ProducerId = input.ProducerId.Value,
            });

        return BuildAuthenticationFailedResult();
    }

    // Uma única resposta para login inexistente, senha errada e falta de vínculo: quem chama não descobre quais contas existem.
    private static MethodResult<AuthenticateCustomerUseCaseOutput> BuildAuthenticationFailedResult()
    {
        var failedNotification = Notification.Error(
            code: AUTHENTICATION_FAILED_NOTIFICATION_CODE,
            message: AUTHENTICATION_FAILED_NOTIFICATION_MESSAGE);

        return MethodResult<AuthenticateCustomerUseCaseOutput>.Error([failedNotification]);
    }

    private static MethodResult<AuthenticateCustomerUseCaseOutput> BuildCanceledResult()
    {
        var canceledNotification = Notification.Error(
            code: AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_CODE,
            message: AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_MESSAGE);

        return MethodResult<AuthenticateCustomerUseCaseOutput>.Error([canceledNotification]);
    }
}
