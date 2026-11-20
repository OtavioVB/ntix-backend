using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.GetOrderPaymentMethods.Inputs;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetOrderPaymentMethods;

/// <summary>
/// Métodos que o checkout pode oferecer para o pedido (REQ7, RN1): os que têm adaptador na Ntix e cujo ranqueamento do produtor do
/// pedido não é vazio. Só o dono do pedido consulta.
/// </summary>
public sealed class GetOrderPaymentMethodsUseCase : IUseCase<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> _rankUseCase;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetOrderPaymentMethodsDiagnostics _diagnostics;
    private readonly ILogger<GetOrderPaymentMethodsUseCase> _logger;

    public GetOrderPaymentMethodsUseCase(
        IOrderRepository orderRepository,
        IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> rankUseCase,
        IUnitOfWork unitOfWork,
        GetOrderPaymentMethodsDiagnostics diagnostics,
        ILogger<GetOrderPaymentMethodsUseCase> logger)
    {
        _orderRepository = orderRepository;
        _rankUseCase = rankUseCase;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string ORDER_NOT_FOUND_NOTIFICATION_CODE = "ORDER_NOT_FOUND";
    private const string ORDER_NOT_FOUND_NOTIFICATION_MESSAGE = "O pedido informado não existe.";

    public const string GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_CODE = "GET_ORDER_PAYMENT_METHODS_CANCELED";
    private const string GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos métodos de pagamento foi cancelada.";

    public async Task<MethodResult<PaymentMethodsOutput>> ExecuteAsync(GetOrderPaymentMethodsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetOrderPaymentMethodsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetOrderPaymentMethods", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetOrderPaymentMethodsDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<PaymentMethodsOutput>.Error(validationResult.Notifications);
            }

            if (!input.Code.IsValid || input.CustomerId is null)
                return NotFound();

            // Só leitura: a transação dá uma conexão ao repositório e é desfeita antes do ranqueamento, que abre a dele.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var order = await _orderRepository.GetByCodeAsync(input.Code.Value, input.CustomerId.Value, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (order is null)
                return NotFound();

            var methods = new List<PaymentMethodOutput>();

            foreach (var method in PaymentMethodsWithAdapter.ALL)
            {
                var ranking = await _rankUseCase.ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(order.ProducerId, method.ToString()), auditableInfo, cancellationToken);

                if (ranking.IsSuccess)
                    methods.Add(new PaymentMethodOutput(method.ToString()));
            }

            RecordOutcome(GetOrderPaymentMethodsDiagnostics.OUTCOME_SERVED);

            GetOrderPaymentMethodsLog.Served(_logger, order.Code, string.Join(", ", methods.Select(method => method.Method)));

            return MethodResult<PaymentMethodsOutput>.Success(new PaymentMethodsOutput(methods));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<PaymentMethodsOutput>.Error([Notification.Error(GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_CODE, GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_MESSAGE)]);

            GetOrderPaymentMethodsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetOrderPaymentMethodsDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private MethodResult<PaymentMethodsOutput> NotFound()
    {
        RecordOutcome(GetOrderPaymentMethodsDiagnostics.OUTCOME_ORDER_NOT_FOUND);

        GetOrderPaymentMethodsLog.NotFound(_logger);

        return MethodResult<PaymentMethodsOutput>.Error([Notification.Error(ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
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
            GetOrderPaymentMethodsLog.RollbackFailed(_logger, ex);
        }
    }
}
