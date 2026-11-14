using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.GetPaymentRoutes.Inputs;
using Application.UseCases.GetPaymentRoutes.Outputs;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetPaymentRoutes;

/// <summary>Rotas da produtora: para cada método, as integrações em ordem de prioridade, inclusive as inativas.</summary>
public sealed class GetPaymentRoutesUseCase : IUseCase<GetPaymentRoutesUseCaseInput, GetPaymentRoutesUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly IPaymentRouteRepository _paymentRouteRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetPaymentRoutesDiagnostics _diagnostics;
    private readonly ILogger<GetPaymentRoutesUseCase> _logger;

    public GetPaymentRoutesUseCase(
        IProducerRepository producerRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        IPaymentRouteRepository paymentRouteRepository,
        IUnitOfWork unitOfWork,
        GetPaymentRoutesDiagnostics diagnostics,
        ILogger<GetPaymentRoutesUseCase> logger)
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

    public const string GET_PAYMENT_ROUTES_CANCELED_NOTIFICATION_CODE = "GET_PAYMENT_ROUTES_CANCELED";
    private const string GET_PAYMENT_ROUTES_CANCELED_NOTIFICATION_MESSAGE = "A consulta das rotas de pagamento foi cancelada.";

    public async Task<MethodResult<GetPaymentRoutesUseCaseOutput>> ExecuteAsync(GetPaymentRoutesUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetPaymentRoutesDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetPaymentRoutes", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var producerSlug = input.ProducerSlug.Value;

            // Só leitura: a transação dá uma conexão aos repositórios e uma leitura consistente, e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(GetPaymentRoutesDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                GetPaymentRoutesLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<GetPaymentRoutesUseCaseOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));
            var integrationsById = (await _producerIntegrationRepository.GetByProducerAsync(producerId.Value, cancellationToken))
                .Select(model => PaymentConversions.ToIntegration(model, providersById))
                .ToDictionary(integration => integration.Id.Value);
            var routeModels = await _paymentRouteRepository.GetByProducerAsync(producerId.Value, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            // Os três métodos sempre presentes: método sem rota sai como lista vazia ("não configurado" na aba).
            var routes = Enum.GetValues<PaymentMethod>().ToDictionary(
                paymentMethod => paymentMethod.ToString(),
                paymentMethod => PaymentConversions.ToOutput(PaymentConversions.ToRoute(producerId.Value, paymentMethod, routeModels, integrationsById)));

            RecordOutcome(GetPaymentRoutesDiagnostics.OUTCOME_SERVED);

            GetPaymentRoutesLog.Served(_logger, producerSlug, string.Join(", ", routes.Select(route => $"{route.Key}={route.Value.Count}")));

            return MethodResult<GetPaymentRoutesUseCaseOutput>.Success(new GetPaymentRoutesUseCaseOutput(routes));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<GetPaymentRoutesUseCaseOutput>.Error([Notification.Error(GET_PAYMENT_ROUTES_CANCELED_NOTIFICATION_CODE, GET_PAYMENT_ROUTES_CANCELED_NOTIFICATION_MESSAGE)]);

            GetPaymentRoutesLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetPaymentRoutesDiagnostics.OUTCOME_ERROR);

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
            GetPaymentRoutesLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<GetPaymentRoutesUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(GetPaymentRoutesDiagnostics.OUTCOME_VALIDATION_FAILED);

        GetPaymentRoutesLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<GetPaymentRoutesUseCaseOutput>.Error(validationResult.Notifications);
    }
}
