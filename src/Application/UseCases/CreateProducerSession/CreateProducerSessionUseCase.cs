using Application.UseCases.Base;
using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Producers;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducerSession.Inputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateProducerSession;

/// <summary>
/// Login do usuário do produtor (REQ5): confere e-mail e senha e abre uma sessão, com access token de 15 minutos e
/// refresh token de 7 dias.
/// </summary>
public sealed class CreateProducerSessionUseCase : IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput>
{
    private readonly IProducerUserRepository _producerUserRepository;
    private readonly IProducerRepository _producerRepository;
    private readonly IProducerUserSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LoginAttemptLimiter _loginAttemptLimiter;
    private readonly ProducerAccessTokenIssuer _accessTokenIssuer;
    private readonly JwtConfiguration _jwtConfiguration;
    private readonly PasswordConfiguration _passwordConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly CreateProducerSessionDiagnostics _diagnostics;
    private readonly CreateProducerDiagnostics _passwordDiagnostics;
    private readonly ILogger<CreateProducerSessionUseCase> _logger;

    public CreateProducerSessionUseCase(
        IProducerUserRepository producerUserRepository,
        IProducerRepository producerRepository,
        IProducerUserSessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        LoginAttemptLimiter loginAttemptLimiter,
        ProducerAccessTokenIssuer accessTokenIssuer,
        JwtConfiguration jwtConfiguration,
        PasswordConfiguration passwordConfiguration,
        TimeProvider timeProvider,
        CreateProducerSessionDiagnostics diagnostics,
        CreateProducerDiagnostics passwordDiagnostics,
        ILogger<CreateProducerSessionUseCase> logger)
    {
        _producerUserRepository = producerUserRepository;
        _producerRepository = producerRepository;
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _loginAttemptLimiter = loginAttemptLimiter;
        _accessTokenIssuer = accessTokenIssuer;
        _jwtConfiguration = jwtConfiguration;
        _passwordConfiguration = passwordConfiguration;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _passwordDiagnostics = passwordDiagnostics;
        _logger = logger;
    }

    public const string INVALID_CREDENTIALS_NOTIFICATION_CODE = "INVALID_CREDENTIALS";
    private const string INVALID_CREDENTIALS_NOTIFICATION_MESSAGE = "E-mail ou senha incorretos.";

    public const string PRODUCER_USER_BLOCKED_NOTIFICATION_CODE = "PRODUCER_USER_BLOCKED";
    private const string PRODUCER_USER_BLOCKED_NOTIFICATION_MESSAGE = "Este acesso está bloqueado. Fale com a Ntix para entender o motivo.";

    public const string TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE = "TOO_MANY_LOGIN_ATTEMPTS";
    private const string TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_MESSAGE = "Muitas tentativas de entrar com este e-mail. Aguarde alguns minutos e tente de novo.";

    public const string CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE = "CREATE_PRODUCER_SESSION_CANCELED";
    private const string CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de login foi cancelada.";

    private const string REASON_INVALID_CREDENTIALS = "INVALID_CREDENTIALS";
    private const string REASON_BLOCKED = "BLOCKED";

    public async Task<MethodResult<ProducerSessionOutput>> ExecuteAsync(CreateProducerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateProducerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateProducerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            // RN12: antes do banco e do hash, igual para e-mail existente e inexistente.
            if (!_loginAttemptLimiter.TryAcquire(input.Email))
                return BuildRateLimitedResult(input);

            // Só leitura: a transação fecha antes do hash, que é lento de propósito e não pode segurar a conexão.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var user = await _producerUserRepository.GetByEmailAsync(input.Email.Value, cancellationToken);
            var producer = user is null ? null : await _producerRepository.GetByIdAsync(user.ProducerId, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var passwordMatches = VerifyPassword(input.Password, user);

            if (user is null || producer is null || !passwordMatches)
                return BuildRejectedResult(input, REASON_INVALID_CREDENTIALS, CreateProducerSessionDiagnostics.OUTCOME_INVALID_CREDENTIALS, INVALID_CREDENTIALS_NOTIFICATION_CODE, INVALID_CREDENTIALS_NOTIFICATION_MESSAGE);

            // RN2: o bloqueio só aparece depois de a senha conferir, para não revelar a conta a quem não a conhece.
            if (ProducerSessionConversions.GetStatus(user) == ProducerUserStatus.BLOCKED)
                return BuildRejectedResult(input, REASON_BLOCKED, CreateProducerSessionDiagnostics.OUTCOME_BLOCKED, PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, PRODUCER_USER_BLOCKED_NOTIFICATION_MESSAGE);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var refreshToken = RefreshTokenValueObject.Generate();

            var session = ProducerUserSession.Start(
                producerUserId: IdValueObject.Build(user.Id.ToString()),
                refreshToken: refreshToken,
                now: now,
                lifetime: _jwtConfiguration.RefreshTokenLifetime,
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            await _sessionRepository.CreateAsync(ProducerSessionConversions.ToModel(session), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            return BuildSuccessResult(user, producer, session, refreshToken, now);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            CreateProducerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateProducerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    /// <summary>
    /// RN3 e RN4: o hash é calculado sempre, com o salt fixo quando o e-mail não existe, para o tempo da resposta não
    /// revelar se a conta existe. A comparação é em tempo constante.
    /// </summary>
    private bool VerifyPassword(PasswordValueObject password, ProducerUserModel? user)
    {
        using var activity = CreateProducerSessionDiagnostics.ACTIVITY_SOURCE.StartActivity("CreateProducerSession.HashPassword");

        activity?.SetTag("ntix.password.iterations", PasswordHashValueObject.ITERATIONS);

        var storedSalt = PasswordSaltValueObject.Build(user?.PasswordSalt);
        var salt = storedSalt.IsValid ? storedSalt : PasswordSaltValueObject.BuildForUnknownUser();

        var startedAt = Stopwatch.GetTimestamp();
        var hash = PasswordHashValueObject.Build(password, salt, _passwordConfiguration.PrivateKey);

        _passwordDiagnostics.PasswordHashed(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        return user is not null && storedSalt.IsValid && hash.Matches(user.PasswordHash);
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
            CreateProducerSessionLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ProducerSessionOutput> BuildSuccessResult(ProducerUserModel user, ProducerModel producer, ProducerUserSession session, RefreshTokenValueObject refreshToken, DateTime now)
    {
        RecordOutcome(CreateProducerSessionDiagnostics.OUTCOME_CREATED);

        Activity.Current?.SetTag("ntix.producer_user_id", user.Id);

        CreateProducerSessionLog.Created(_logger, producerUserId: user.Id, producerSlug: producer.Slug, sessionId: session.Id.Value);

        return MethodResult<ProducerSessionOutput>.Success(ProducerSessionConversions.ToOutput(_accessTokenIssuer, user, producer, session, refreshToken, now));
    }

    private MethodResult<ProducerSessionOutput> BuildRejectedResult(CreateProducerSessionUseCaseInput input, string reason, string outcome, string code, string message)
    {
        RecordOutcome(outcome);

        CreateProducerSessionLog.Rejected(_logger, reason: reason, obfuscatedEmail: input.Email.GetObfuscatedValue());

        return MethodResult<ProducerSessionOutput>.Error([Notification.Error(code, message)]);
    }

    private MethodResult<ProducerSessionOutput> BuildRateLimitedResult(CreateProducerSessionUseCaseInput input)
    {
        RecordOutcome(CreateProducerSessionDiagnostics.OUTCOME_RATE_LIMITED);

        _diagnostics.RateLimited();

        CreateProducerSessionLog.RateLimited(_logger, input.Email.GetObfuscatedValue());

        return MethodResult<ProducerSessionOutput>.Error([Notification.Error(TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE, TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_MESSAGE)]);
    }

    private MethodResult<ProducerSessionOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(CreateProducerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateProducerSessionLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ProducerSessionOutput>.Error(validationResult.Notifications);
    }

    private static MethodResult<ProducerSessionOutput> BuildCanceledResult()
        => MethodResult<ProducerSessionOutput>.Error([Notification.Error(CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);
}
