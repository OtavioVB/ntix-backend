using Application.UseCases.Base;
using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Customers;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomerSession.Inputs;
using Domain.BoundedContexts.CustomerContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateCustomerSession;

/// <summary>
/// Login do cliente (REQ12): e-mail ou CPF e senha. Abre uma sessão com access token de 15 minutos e refresh token de
/// 30 dias, como a do produtor (REQ5).
/// </summary>
public sealed class CreateCustomerSessionUseCase : IUseCase<CreateCustomerSessionUseCaseInput, CustomerSessionOutput>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoginAttemptLimiter _loginAttemptLimiter;
    private readonly CustomerAccessTokenIssuer _accessTokenIssuer;
    private readonly CustomerSessionConfiguration _sessionConfiguration;
    private readonly PasswordConfiguration _passwordConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly CreateCustomerSessionDiagnostics _diagnostics;
    private readonly CreateCustomerDiagnostics _passwordDiagnostics;
    private readonly ILogger<CreateCustomerSessionUseCase> _logger;

    public CreateCustomerSessionUseCase(
        ICustomerRepository customerRepository,
        ICustomerSessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        LoginAttemptLimiter loginAttemptLimiter,
        CustomerAccessTokenIssuer accessTokenIssuer,
        CustomerSessionConfiguration sessionConfiguration,
        PasswordConfiguration passwordConfiguration,
        TimeProvider timeProvider,
        CreateCustomerSessionDiagnostics diagnostics,
        CreateCustomerDiagnostics passwordDiagnostics,
        ILogger<CreateCustomerSessionUseCase> logger)
    {
        _customerRepository = customerRepository;
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _loginAttemptLimiter = loginAttemptLimiter;
        _accessTokenIssuer = accessTokenIssuer;
        _sessionConfiguration = sessionConfiguration;
        _passwordConfiguration = passwordConfiguration;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _passwordDiagnostics = passwordDiagnostics;
        _logger = logger;
    }

    public const string INVALID_CREDENTIALS_NOTIFICATION_CODE = "INVALID_CREDENTIALS";
    private const string INVALID_CREDENTIALS_NOTIFICATION_MESSAGE = "E-mail, CPF ou senha incorretos.";

    public const string TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE = "TOO_MANY_LOGIN_ATTEMPTS";
    private const string TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_MESSAGE = "Muitas tentativas de entrar com esta conta. Aguarde alguns minutos e tente de novo.";

    public const string CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE = "CREATE_CUSTOMER_SESSION_CANCELED";
    private const string CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de login foi cancelada.";

    public async Task<MethodResult<CustomerSessionOutput>> ExecuteAsync(CreateCustomerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateCustomerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateCustomerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(CreateCustomerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

                CreateCustomerSessionLog.InvalidData(_logger, string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<CustomerSessionOutput>.Error(validationResult.Notifications);
            }

            // RN9: antes do banco e do hash, igual para conta existente e inexistente.
            if (!_loginAttemptLimiter.TryAcquire(input.Login.AttemptKey))
            {
                RecordOutcome(CreateCustomerSessionDiagnostics.OUTCOME_RATE_LIMITED);

                _diagnostics.RateLimited();

                CreateCustomerSessionLog.RateLimited(_logger, input.Login.GetObfuscatedValue());

                return MethodResult<CustomerSessionOutput>.Error([Notification.Error(TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE, TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_MESSAGE)]);
            }

            // Só leitura: a transação fecha antes do hash, que é lento de propósito e não pode segurar a conexão.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var customer = input.Login.Kind switch
            {
                CustomerLoginKind.DOCUMENT => await _customerRepository.GetByDocumentAsync(input.Login.Value, cancellationToken),
                CustomerLoginKind.EMAIL => await _customerRepository.GetByEmailAsync(input.Login.Value, cancellationToken),
                _ => null,
            };

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (!VerifyPassword(input.Password, customer) || customer is null)
            {
                RecordOutcome(CreateCustomerSessionDiagnostics.OUTCOME_INVALID_CREDENTIALS);

                CreateCustomerSessionLog.Rejected(_logger, input.Login.Kind.ToString(), input.Login.GetObfuscatedValue());

                return MethodResult<CustomerSessionOutput>.Error([Notification.Error(INVALID_CREDENTIALS_NOTIFICATION_CODE, INVALID_CREDENTIALS_NOTIFICATION_MESSAGE)]);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var refreshToken = RefreshTokenValueObject.Generate();
            var session = CustomerSession.Start(
                customerId: IdValueObject.Build(customer.Id.ToString()),
                refreshToken: refreshToken,
                now: now,
                lifetime: _sessionConfiguration.RefreshTokenLifetime,
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _sessionRepository.CreateAsync(CustomerSessionConversions.ToModel(session), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(CreateCustomerSessionDiagnostics.OUTCOME_CREATED);

            CreateCustomerSessionLog.Created(_logger, customer.Id, session.Id.Value);

            return MethodResult<CustomerSessionOutput>.Success(CustomerSessionConversions.ToOutput(_accessTokenIssuer, customer, session, refreshToken, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<CustomerSessionOutput>.Error([Notification.Error(CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);

            CreateCustomerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateCustomerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    /// <summary>
    /// RN2 e RN3: o hash é calculado sempre, com o salt fixo quando a conta não existe, para o tempo da resposta não revelar
    /// se ela existe. A comparação é em tempo constante.
    /// </summary>
    private bool VerifyPassword(PasswordValueObject password, CustomerModel? customer)
    {
        using var activity = CreateCustomerSessionDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateCustomerSession.HashPassword");

        activity?.SetTag("ntix.password.iterations", PasswordHashValueObject.ITERATIONS);

        var storedSalt = PasswordSaltValueObject.Build(customer?.PasswordSalt);
        var salt = storedSalt.IsValid ? storedSalt : PasswordSaltValueObject.BuildForUnknownUser();

        var startedAt = Stopwatch.GetTimestamp();
        var hash = PasswordHashValueObject.Build(password, salt, _passwordConfiguration.PrivateKey);

        _passwordDiagnostics.PasswordHashed(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        return customer is not null && storedSalt.IsValid && hash.Matches(customer.PasswordHash);
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.Finished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            CreateCustomerSessionLog.RollbackFailed(_logger, ex);
        }
    }
}
