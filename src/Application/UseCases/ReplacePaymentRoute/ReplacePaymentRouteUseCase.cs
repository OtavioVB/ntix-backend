using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.ReplacePaymentRoute;

/// <summary>
/// Substitui, em uma transação, a lista inteira de integrações de um método (RN6). A nova prioridade vale só para as
/// próximas cobranças: o pagamento já iniciado continua na integração que o processou.
/// </summary>
public sealed class ReplacePaymentRouteUseCase : IUseCase<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly IPaymentRouteRepository _paymentRouteRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReplacePaymentRouteDiagnostics _diagnostics;
    private readonly ILogger<ReplacePaymentRouteUseCase> _logger;

    public ReplacePaymentRouteUseCase(
        IProducerRepository producerRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        IPaymentRouteRepository paymentRouteRepository,
        IUnitOfWork unitOfWork,
        ReplacePaymentRouteDiagnostics diagnostics,
        ILogger<ReplacePaymentRouteUseCase> logger)
    {
        _producerRepository = producerRepository;
        _paymentProviderRepository = paymentProviderRepository;
        _producerIntegrationRepository = producerIntegrationRepository;
        _paymentRouteRepository = paymentRouteRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string REPLACE_PAYMENT_ROUTE_CANCELED_NOTIFICATION_CODE = "REPLACE_PAYMENT_ROUTE_CANCELED";
    private const string REPLACE_PAYMENT_ROUTE_CANCELED_NOTIFICATION_MESSAGE = "A alteração da rota de pagamento foi cancelada.";

    public async Task<MethodResult<ReplacePaymentRouteUseCaseOutput>> ExecuteAsync(ReplacePaymentRouteUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = ReplacePaymentRouteDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("ReplacePaymentRoute", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(input, validationResult);

            var producerSlug = input.ProducerSlug.Value;
            var paymentMethod = input.PaymentMethod.Value;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(ReplacePaymentRouteDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                ReplacePaymentRouteLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<ReplacePaymentRouteUseCaseOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));

            // Só as integrações deste produtor entram na validação: a de outro produtor é "não encontrada" (RN1).
            var producerIntegrations = (await _producerIntegrationRepository.GetByProducerAsync(producerId.Value, cancellationToken))
                .Select(model => PaymentConversions.ToIntegration(model, providersById))
                .ToArray();

            var routeResult = PaymentRoute.Build(
                producerId: IdValueObject.Build(producerId.Value.ToString()),
                paymentMethod: paymentMethod,
                integrationIds: input.IntegrationIds!,
                producerIntegrations: producerIntegrations);

            if (routeResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, routeResult);
            }

            var route = routeResult.Output!;

            await _paymentRouteRepository.ReplaceAsync(
                producerId: producerId.Value,
                paymentMethod: paymentMethod.ToString(),
                routes: PaymentConversions.ToModels(route, DateTimeValueObject.Build(DateTime.UtcNow), IdValueObject.Build(auditableInfo.CorrelationId.ToString())),
                cancellationToken: cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(ReplacePaymentRouteDiagnostics.OUTCOME_REPLACED);

            ReplacePaymentRouteLog.Replaced(
                logger: _logger,
                paymentMethod: paymentMethod.ToString(),
                producerSlug: producerSlug,
                integrations: string.Join(", ", route.Integrations.Select((integration, index) => $"{PaymentRoute.GetPriority(index)}: {integration.Provider.Code} {integration.Id.Value}")));

            return MethodResult<ReplacePaymentRouteUseCaseOutput>.Success(new ReplacePaymentRouteUseCaseOutput(PaymentConversions.ToOutput(route)));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ReplacePaymentRouteUseCaseOutput>.Error([Notification.Error(REPLACE_PAYMENT_ROUTE_CANCELED_NOTIFICATION_CODE, REPLACE_PAYMENT_ROUTE_CANCELED_NOTIFICATION_MESSAGE)]);

            ReplacePaymentRouteLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(ReplacePaymentRouteDiagnostics.OUTCOME_ERROR);

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
            ReplacePaymentRouteLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ReplacePaymentRouteUseCaseOutput> BuildInvalidDataResult(ReplacePaymentRouteUseCaseInput input, MethodResult validationResult)
    {
        RecordOutcome(ReplacePaymentRouteDiagnostics.OUTCOME_VALIDATION_FAILED);

        ReplacePaymentRouteLog.InvalidData(
            logger: _logger,
            producerSlug: input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-",
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ReplacePaymentRouteUseCaseOutput>.Error(validationResult.Notifications);
    }
}
