using Application.UseCases.Base;
using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Producers;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.RefreshProducerSession.Inputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.RefreshProducerSession;

/// <summary>
/// Renovação da sessão do usuário do produtor (REQ5): troca o refresh token por um par novo, da mesma família (RN7),
/// relendo o usuário para recusar o bloqueado (RN8). Token já substituído reapresentado revoga a família inteira.
/// </summary>
public sealed class RefreshProducerSessionUseCase : IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput>
{
    private readonly IProducerUserSessionRepository _sessionRepository;
    private readonly IProducerUserRepository _producerUserRepository;
    private readonly IProducerRepository _producerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ProducerAccessTokenIssuer _accessTokenIssuer;
    private readonly JwtConfiguration _jwtConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshProducerSessionDiagnostics _diagnostics;
    private readonly ILogger<RefreshProducerSessionUseCase> _logger;

    public RefreshProducerSessionUseCase(
        IProducerUserSessionRepository sessionRepository,
        IProducerUserRepository producerUserRepository,
        IProducerRepository producerRepository,
        IUnitOfWork unitOfWork,
        ProducerAccessTokenIssuer accessTokenIssuer,
        JwtConfiguration jwtConfiguration,
        TimeProvider timeProvider,
        RefreshProducerSessionDiagnostics diagnostics,
        ILogger<RefreshProducerSessionUseCase> logger)
    {
        _sessionRepository = sessionRepository;
        _producerUserRepository = producerUserRepository;
        _producerRepository = producerRepository;
        _unitOfWork = unitOfWork;
        _accessTokenIssuer = accessTokenIssuer;
        _jwtConfiguration = jwtConfiguration;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string INVALID_REFRESH_TOKEN_NOTIFICATION_CODE = "INVALID_REFRESH_TOKEN";
    private const string INVALID_REFRESH_TOKEN_NOTIFICATION_MESSAGE = "Sua sessão expirou. Entre de novo para continuar.";

    public const string PRODUCER_USER_BLOCKED_NOTIFICATION_CODE = "PRODUCER_USER_BLOCKED";
    private const string PRODUCER_USER_BLOCKED_NOTIFICATION_MESSAGE = "Este acesso está bloqueado. Fale com a Ntix para entender o motivo.";

    public const string REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE = "REFRESH_PRODUCER_SESSION_CANCELED";
    private const string REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A renovação da sessão foi cancelada.";

    private const string STATE_MALFORMED = "fora do formato";
    private const string STATE_NOT_FOUND = "inexistente";
    private const string STATE_REVOKED = "revogado";
    private const string STATE_EXPIRED = "expirado";
    private const string STATE_USER_NOT_FOUND = "de usuário inexistente";

    public async Task<MethodResult<ProducerSessionOutput>> ExecuteAsync(RefreshProducerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = RefreshProducerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("RefreshProducerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (auditableInfo.MethodResult.IsError)
                return BuildInvalidDataResult(auditableInfo.MethodResult);

            if (!input.RefreshToken.IsValid)
                return BuildInvalidTokenResult(STATE_MALFORMED);

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var sessionModel = await _sessionRepository.GetByRefreshTokenHashAsync(input.RefreshToken.Hash, cancellationToken);

            if (sessionModel is null)
                return await RejectInvalidTokenAsync(STATE_NOT_FOUND);

            var session = ProducerSessionConversions.ToEntity(sessionModel);

            switch (session.GetState(now))
            {
                case ProducerUserSessionState.REPLACED:
                    return await RevokeReusedFamilyAsync(session, now, cancellationToken);
                case ProducerUserSessionState.REVOKED:
                    return await RejectInvalidTokenAsync(STATE_REVOKED);
                case ProducerUserSessionState.EXPIRED:
                    return await RejectInvalidTokenAsync(STATE_EXPIRED);
            }

            // RN8: o status e o slug valem o que está no banco agora, não o que valia no login.
            var user = await _producerUserRepository.GetByIdAsync(session.ProducerUserId.Value, cancellationToken);
            var producer = user is null ? null : await _producerRepository.GetByIdAsync(user.ProducerId, cancellationToken);

            if (user is null || producer is null)
                return await RejectInvalidTokenAsync(STATE_USER_NOT_FOUND);

            if (ProducerSessionConversions.GetStatus(user) == ProducerUserStatus.BLOCKED)
                return await RejectBlockedAsync(user.Id);

            var refreshToken = RefreshTokenValueObject.Generate();
            var (replaced, next) = session.Rotate(
                refreshToken: refreshToken,
                now: now,
                lifetime: _jwtConfiguration.RefreshTokenLifetime,
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            await _sessionRepository.ReplaceAsync(ProducerSessionConversions.ToModel(replaced), ProducerSessionConversions.ToModel(next), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_REFRESHED);

            RefreshProducerSessionLog.Refreshed(_logger, sessionId: session.Id.Value, producerUserId: user.Id, nextSessionId: next.Id.Value, familyId: session.FamilyId.Value);

            return MethodResult<ProducerSessionOutput>.Success(ProducerSessionConversions.ToOutput(_accessTokenIssuer, user, producer, next, refreshToken, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            RefreshProducerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    // RN7: quem reapresenta um token já trocado tem uma cópia dele. A regra manda revogar tudo, então aqui há commit.
    private async Task<MethodResult<ProducerSessionOutput>> RevokeReusedFamilyAsync(ProducerUserSession session, DateTime now, CancellationToken cancellationToken)
    {
        var revokedCount = await _sessionRepository.RevokeFamilyAsync(session.FamilyId.Value, now, cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_REUSE_DETECTED);

        _diagnostics.ReuseDetected();

        RefreshProducerSessionLog.ReuseDetected(_logger, sessionId: session.Id.Value, producerUserId: session.ProducerUserId.Value, familyId: session.FamilyId.Value, revokedCount: revokedCount);

        return InvalidTokenError();
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
            RefreshProducerSessionLog.RollbackFailed(_logger, ex);
        }
    }

    private async Task<MethodResult<ProducerSessionOutput>> RejectInvalidTokenAsync(string state)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        return BuildInvalidTokenResult(state);
    }

    private MethodResult<ProducerSessionOutput> BuildInvalidTokenResult(string state)
    {
        RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_INVALID_TOKEN);

        RefreshProducerSessionLog.InvalidToken(_logger, state);

        return InvalidTokenError();
    }

    private async Task<MethodResult<ProducerSessionOutput>> RejectBlockedAsync(Guid producerUserId)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_BLOCKED);

        RefreshProducerSessionLog.Blocked(_logger, producerUserId);

        return MethodResult<ProducerSessionOutput>.Error([Notification.Error(PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, PRODUCER_USER_BLOCKED_NOTIFICATION_MESSAGE)]);
    }

    private MethodResult<ProducerSessionOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(RefreshProducerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

        RefreshProducerSessionLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ProducerSessionOutput>.Error(validationResult.Notifications);
    }

    private static MethodResult<ProducerSessionOutput> InvalidTokenError()
        => MethodResult<ProducerSessionOutput>.Error([Notification.Error(INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, INVALID_REFRESH_TOKEN_NOTIFICATION_MESSAGE)]);

    private static MethodResult<ProducerSessionOutput> BuildCanceledResult()
        => MethodResult<ProducerSessionOutput>.Error([Notification.Error(REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);
}
