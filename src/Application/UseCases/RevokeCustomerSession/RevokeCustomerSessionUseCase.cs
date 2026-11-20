using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.RevokeCustomerSession.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.RevokeCustomerSession;

/// <summary>
/// Logout do cliente (REQ12, RN5): revoga a família inteira do refresh token. Token desconhecido também é sucesso, para a
/// resposta não revelar se ele existe.
/// </summary>
public sealed class RevokeCustomerSessionUseCase : IUseCase<RevokeCustomerSessionUseCaseInput>
{
    private readonly ICustomerSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly RevokeCustomerSessionDiagnostics _diagnostics;
    private readonly ILogger<RevokeCustomerSessionUseCase> _logger;

    public RevokeCustomerSessionUseCase(
        ICustomerSessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        RevokeCustomerSessionDiagnostics diagnostics,
        ILogger<RevokeCustomerSessionUseCase> logger)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const string REVOKE_CUSTOMER_SESSION_SUCCESS_NOTIFICATION_CODE = "REVOKE_CUSTOMER_SESSION_SUCCESS";
    private const string REVOKE_CUSTOMER_SESSION_SUCCESS_NOTIFICATION_MESSAGE = "Você saiu da Ntix.";

    public const string REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE = "REVOKE_CUSTOMER_SESSION_CANCELED";
    private const string REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A solicitação de saída foi cancelada.";

    public async Task<MethodResult> ExecuteAsync(RevokeCustomerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = RevokeCustomerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("RevokeCustomerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (auditableInfo.MethodResult.IsError)
            {
                RecordOutcome(RevokeCustomerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult.Error(auditableInfo.MethodResult.Notifications);
            }

            if (!input.RefreshToken.IsValid)
                return NotFound();

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var session = await _sessionRepository.GetByRefreshTokenHashAsync(input.RefreshToken.Hash, cancellationToken);

            if (session is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return NotFound();
            }

            var revokedCount = await _sessionRepository.RevokeFamilyAsync(session.FamilyId, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(RevokeCustomerSessionDiagnostics.OUTCOME_REVOKED);

            RevokeCustomerSessionLog.Revoked(_logger, session.CustomerId, session.FamilyId, revokedCount);

            return Success();
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult.Error([Notification.Error(REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);

            RevokeCustomerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(RevokeCustomerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private MethodResult NotFound()
    {
        RecordOutcome(RevokeCustomerSessionDiagnostics.OUTCOME_NOT_FOUND);

        RevokeCustomerSessionLog.NotFound(_logger);

        return Success();
    }

    private static MethodResult Success()
        => MethodResult.Success([Notification.Success(REVOKE_CUSTOMER_SESSION_SUCCESS_NOTIFICATION_CODE, REVOKE_CUSTOMER_SESSION_SUCCESS_NOTIFICATION_MESSAGE)]);

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
            RevokeCustomerSessionLog.RollbackFailed(_logger, ex);
        }
    }
}
