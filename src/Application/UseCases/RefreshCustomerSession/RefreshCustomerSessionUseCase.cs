using Application.UseCases.Base;
using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Customers;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Domain.BoundedContexts.CustomerContext.Entities;
using Domain.BoundedContexts.CustomerContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.RefreshCustomerSession;

/// <summary>
/// Renovação da sessão do cliente (REQ12, RN5): troca o refresh token por um par novo da mesma família. Reapresentar um
/// token já trocado revoga a família inteira, porque indica roubo.
/// </summary>
public sealed class RefreshCustomerSessionUseCase : IUseCase<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ICustomerSessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CustomerAccessTokenIssuer _accessTokenIssuer;
    private readonly CustomerSessionConfiguration _sessionConfiguration;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshCustomerSessionDiagnostics _diagnostics;
    private readonly ILogger<RefreshCustomerSessionUseCase> _logger;

    public RefreshCustomerSessionUseCase(
        ICustomerRepository customerRepository,
        ICustomerSessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        CustomerAccessTokenIssuer accessTokenIssuer,
        CustomerSessionConfiguration sessionConfiguration,
        TimeProvider timeProvider,
        RefreshCustomerSessionDiagnostics diagnostics,
        ILogger<RefreshCustomerSessionUseCase> logger)
    {
        _customerRepository = customerRepository;
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _accessTokenIssuer = accessTokenIssuer;
        _sessionConfiguration = sessionConfiguration;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string INVALID_REFRESH_TOKEN_NOTIFICATION_CODE = "INVALID_REFRESH_TOKEN";
    private const string INVALID_REFRESH_TOKEN_NOTIFICATION_MESSAGE = "Sua sessão expirou. Entre de novo para continuar.";

    public const string REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE = "REFRESH_CUSTOMER_SESSION_CANCELED";
    private const string REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE = "A renovação da sessão foi cancelada.";

    private const string STATE_MALFORMED = "fora do formato";
    private const string STATE_NOT_FOUND = "inexistente";
    private const string STATE_REVOKED = "revogado";
    private const string STATE_EXPIRED = "expirado";
    private const string STATE_CUSTOMER_NOT_FOUND = "de cliente inexistente";

    public async Task<MethodResult<CustomerSessionOutput>> ExecuteAsync(RefreshCustomerSessionUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = RefreshCustomerSessionDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("RefreshCustomerSession", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (auditableInfo.MethodResult.IsError)
            {
                RecordOutcome(RefreshCustomerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<CustomerSessionOutput>.Error(auditableInfo.MethodResult.Notifications);
            }

            if (!input.RefreshToken.IsValid)
                return InvalidToken(STATE_MALFORMED);

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var sessionModel = await _sessionRepository.GetByRefreshTokenHashAsync(input.RefreshToken.Hash, cancellationToken);

            if (sessionModel is null)
                return await RejectAsync(STATE_NOT_FOUND);

            var session = CustomerSessionConversions.ToEntity(sessionModel);

            switch (session.GetState(now))
            {
                case CustomerSessionState.REPLACED:
                    return await RevokeReusedFamilyAsync(session, now, cancellationToken);
                case CustomerSessionState.REVOKED:
                    return await RejectAsync(STATE_REVOKED);
                case CustomerSessionState.EXPIRED:
                    return await RejectAsync(STATE_EXPIRED);
            }

            var customer = await _customerRepository.GetByIdAsync(session.CustomerId.Value, cancellationToken);

            if (customer is null)
                return await RejectAsync(STATE_CUSTOMER_NOT_FOUND);

            var refreshToken = RefreshTokenValueObject.Generate();
            var (replaced, next) = session.Rotate(refreshToken, now, _sessionConfiguration.RefreshTokenLifetime, IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            await _sessionRepository.ReplaceAsync(CustomerSessionConversions.ToModel(replaced), CustomerSessionConversions.ToModel(next), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(RefreshCustomerSessionDiagnostics.OUTCOME_REFRESHED);

            RefreshCustomerSessionLog.Refreshed(_logger, session.Id.Value, customer.Id, next.Id.Value, session.FamilyId.Value);

            return MethodResult<CustomerSessionOutput>.Success(CustomerSessionConversions.ToOutput(_accessTokenIssuer, customer, next, refreshToken, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<CustomerSessionOutput>.Error([Notification.Error(REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_MESSAGE)]);

            RefreshCustomerSessionLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(RefreshCustomerSessionDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    // RN5: o reuso de um token já trocado revoga a família inteira, e o cliente precisa entrar com a senha de novo.
    private async Task<MethodResult<CustomerSessionOutput>> RevokeReusedFamilyAsync(CustomerSession session, DateTime now, CancellationToken cancellationToken)
    {
        var revokedCount = await _sessionRepository.RevokeFamilyAsync(session.FamilyId.Value, now, cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        RecordOutcome(RefreshCustomerSessionDiagnostics.OUTCOME_REUSE_DETECTED);

        _diagnostics.ReuseDetected();

        RefreshCustomerSessionLog.ReuseDetected(_logger, session.CustomerId.Value, session.FamilyId.Value, revokedCount);

        return MethodResult<CustomerSessionOutput>.Error([Notification.Error(INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, INVALID_REFRESH_TOKEN_NOTIFICATION_MESSAGE)]);
    }

    private async Task<MethodResult<CustomerSessionOutput>> RejectAsync(string state)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        return InvalidToken(state);
    }

    private MethodResult<CustomerSessionOutput> InvalidToken(string state)
    {
        RecordOutcome(RefreshCustomerSessionDiagnostics.OUTCOME_INVALID_TOKEN);

        RefreshCustomerSessionLog.InvalidToken(_logger, state);

        return MethodResult<CustomerSessionOutput>.Error([Notification.Error(INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, INVALID_REFRESH_TOKEN_NOTIFICATION_MESSAGE)]);
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
            RefreshCustomerSessionLog.RollbackFailed(_logger, ex);
        }
    }
}
