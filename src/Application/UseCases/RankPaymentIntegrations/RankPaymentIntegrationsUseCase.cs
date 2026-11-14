using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.RankPaymentIntegrations;

/// <summary>
/// Integrações por onde cobrar um método do produtor, em ordem de prioridade (RN8). É o que o pagamento (REQ7) e o
/// checkout consomem; neste requisito não tem endpoint. Ranqueamento vazio devolve PAYMENT_METHOD_UNAVAILABLE.
/// </summary>
public sealed class RankPaymentIntegrationsUseCase : IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>
{
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly IPaymentRouteRepository _paymentRouteRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RankPaymentIntegrationsDiagnostics _diagnostics;
    private readonly ILogger<RankPaymentIntegrationsUseCase> _logger;

    public RankPaymentIntegrationsUseCase(
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        IPaymentRouteRepository paymentRouteRepository,
        IUnitOfWork unitOfWork,
        RankPaymentIntegrationsDiagnostics diagnostics,
        ILogger<RankPaymentIntegrationsUseCase> logger)
    {
        _paymentProviderRepository = paymentProviderRepository;
        _producerIntegrationRepository = producerIntegrationRepository;
        _paymentRouteRepository = paymentRouteRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string RANK_PAYMENT_INTEGRATIONS_CANCELED_NOTIFICATION_CODE = "RANK_PAYMENT_INTEGRATIONS_CANCELED";
    private const string RANK_PAYMENT_INTEGRATIONS_CANCELED_NOTIFICATION_MESSAGE = "O ranqueamento das integrações de pagamento foi cancelado.";

    public async Task<MethodResult<RankPaymentIntegrationsUseCaseOutput>> ExecuteAsync(RankPaymentIntegrationsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = RankPaymentIntegrationsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("RankPaymentIntegrations", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var producerId = input.ProducerId.Value;
            var paymentMethod = input.PaymentMethod.Value;

            activity?.SetTag("ntix.payments.method", paymentMethod.ToString());

            // Só leitura, sem chamar o fornecedor (RN12): a disponibilidade em tempo de cobrança é do fallback do REQ7.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var routeModels = await _paymentRouteRepository.GetByProducerAndMethodAsync(producerId, paymentMethod.ToString(), cancellationToken);
            var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));
            var integrationsById = routeModels.Count == 0
                ? new Dictionary<Guid, ProducerIntegration>()
                : (await _producerIntegrationRepository.GetByProducerAsync(producerId, cancellationToken))
                    .Select(model => PaymentConversions.ToIntegration(model, providersById))
                    .ToDictionary(integration => integration.Id.Value);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var route = PaymentConversions.ToRoute(producerId, paymentMethod, routeModels, integrationsById);
            var rankingResult = PaymentIntegrationRanking.Rank(route);

            if (rankingResult.IsError)
            {
                RecordOutcome(RankPaymentIntegrationsDiagnostics.OUTCOME_UNAVAILABLE);

                _diagnostics.Empty(paymentMethod.ToString(), route.Integrations.FirstOrDefault()?.Provider.Code ?? RankPaymentIntegrationsDiagnostics.NO_PROVIDER);

                RankPaymentIntegrationsLog.Unavailable(_logger, paymentMethod.ToString(), producerId, route.Integrations.Count);

                return MethodResult<RankPaymentIntegrationsUseCaseOutput>.Error(rankingResult.Notifications);
            }

            var priorities = route.Integrations
                .Select((integration, index) => (integration.Id.Value, Priority: PaymentRoute.GetPriority(index)))
                .ToDictionary(item => item.Value, item => item.Priority);

            var ranked = rankingResult.Output!
                .Select(integration => new RankedIntegrationOutput(priorities[integration.Id.Value], integration.Id.Value, integration.Provider.Code))
                .ToArray();

            RecordOutcome(RankPaymentIntegrationsDiagnostics.OUTCOME_RANKED);

            RankPaymentIntegrationsLog.Ranked(
                logger: _logger,
                paymentMethod: paymentMethod.ToString(),
                producerId: producerId,
                integrations: string.Join(", ", ranked.Select(integration => $"{integration.Priority}: {integration.ProviderCode} {integration.IntegrationId}")));

            return MethodResult<RankPaymentIntegrationsUseCaseOutput>.Success(new RankPaymentIntegrationsUseCaseOutput(paymentMethod.ToString(), ranked));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<RankPaymentIntegrationsUseCaseOutput>.Error([Notification.Error(RANK_PAYMENT_INTEGRATIONS_CANCELED_NOTIFICATION_CODE, RANK_PAYMENT_INTEGRATIONS_CANCELED_NOTIFICATION_MESSAGE)]);

            RankPaymentIntegrationsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(RankPaymentIntegrationsDiagnostics.OUTCOME_ERROR);

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
            RankPaymentIntegrationsLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<RankPaymentIntegrationsUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(RankPaymentIntegrationsDiagnostics.OUTCOME_VALIDATION_FAILED);

        RankPaymentIntegrationsLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<RankPaymentIntegrationsUseCaseOutput>.Error(validationResult.Notifications);
    }
}
