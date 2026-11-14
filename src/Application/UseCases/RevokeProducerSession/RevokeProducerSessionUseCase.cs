using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.RevokeProducerSession;

/// <summary>
/// Logout do usuário do produtor (REQ5, RN11): revoga a família inteira do refresh token informado. Idempotente: token
/// inexistente ou já revogado também é sucesso.
/// </summary>
public sealed class RevokeProducerSessionUseCase : IUseCase<RevokeProducerSessionUseCaseInput>
{
    private readonly IProducerUserSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly RevokeProducerSessionDiagnostics _diagnostics;
    private readonly ILogger<RevokeProducerSessionUseCase> _logger;

    public RevokeProducerSessionUseCase(
        IProducerUserSessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        RevokeProducerSessionDiagnostics diagnostics,
        ILogger<RevokeProducerSessionUseCase> logger)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string REVOKE_PRODUCER_SESSION_SUCCESS_NOTIFICATION_CODE = "REVOKE_PRODUCER_SESSION_SUCCESS";
    private const string REVOKE_PRODUCER_SESSION_SUCCESS_NOTIFICATION_MESSAGE = "Você saiu da Ntix.";

    public const string REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE = "REVOKE_PRODUCER_SESSION_CANCELED";
    private const string REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de saída foi cancelada.";

    public async Task<MethodResult> ExecuteAsync(RevokeProducerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = RevokeProducerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("RevokeProducerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (auditableInfo.MethodResult.IsError)
                return BuildInvalidDataResult(auditableInfo.MethodResult);

            if (!input.RefreshToken.IsValid)
                return BuildNotFoundResult();

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var session = await _sessionRepository.GetByRefreshTokenHashAsync(input.RefreshToken.Hash, cancellationToken);

            if (session is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildNotFoundResult();
            }

            var revokedCount = await _sessionRepository.RevokeFamilyAsync(session.FamilyId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(RevokeProducerSessionDiagnostics.OUTCOME_REVOKED);

            RevokeProducerSessionLog.Revoked(_logger, producerUserId: session.ProducerUserId, familyId: session.FamilyId, revokedCount: revokedCount);

            return BuildSuccessResult();
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            RevokeProducerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(RevokeProducerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
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
            RevokeProducerSessionLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult BuildNotFoundResult()
    {
        RecordOutcome(RevokeProducerSessionDiagnostics.OUTCOME_NOT_FOUND);

        RevokeProducerSessionLog.NotFound(_logger);

        return BuildSuccessResult();
    }

    private static MethodResult BuildSuccessResult()
        => MethodResult.Success([Notification.Success(REVOKE_PRODUCER_SESSION_SUCCESS_NOTIFICATION_CODE, REVOKE_PRODUCER_SESSION_SUCCESS_NOTIFICATION_MESSAGE)]);

    private MethodResult BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(RevokeProducerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

        RevokeProducerSessionLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return validationResult;
    }

    private static MethodResult BuildCanceledResult()
        => MethodResult.Error([Notification.Error(REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);
}
