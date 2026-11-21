using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.GetCustomerOrder.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Application.UseCases.Base.Payments;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetCustomerOrder;

/// <summary>
/// Consulta do pedido pelo código, usada pelo checkout para acompanhar o status (REQ6). O cliente só vê os próprios pedidos (RN11),
/// e a reserva vencida aparece como EXPIRED (RN12).
/// </summary>
public sealed class GetCustomerOrderUseCase : IUseCase<GetCustomerOrderUseCaseInput, OrderOutput>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly GetCustomerOrderDiagnostics _diagnostics;
    private readonly ILogger<GetCustomerOrderUseCase> _logger;

    public GetCustomerOrderUseCase(
        IOrderRepository orderRepository,
        IEventRepository eventRepository,
        IPaymentRepository paymentRepository,
        ITicketRepository ticketRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        GetCustomerOrderDiagnostics diagnostics,
        ILogger<GetCustomerOrderUseCase> logger)
    {
        _orderRepository = orderRepository;
        _eventRepository = eventRepository;
        _paymentRepository = paymentRepository;
        _ticketRepository = ticketRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string ORDER_NOT_FOUND_NOTIFICATION_CODE = "ORDER_NOT_FOUND";
    private const string ORDER_NOT_FOUND_NOTIFICATION_MESSAGE = "O pedido informado não existe.";

    public const string GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_CODE = "GET_CUSTOMER_ORDER_CANCELED";
    private const string GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_MESSAGE = "A consulta do pedido foi cancelada.";

    public async Task<MethodResult<OrderOutput>> ExecuteAsync(GetCustomerOrderUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetCustomerOrderDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetCustomerOrder", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetCustomerOrderDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<OrderOutput>.Error(validationResult.Notifications);
            }

            if (!input.Code.IsValid || input.CustomerId is null)
                return NotFound();

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            // Só leitura: a transação dá uma conexão aos repositórios e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var orderModel = await _orderRepository.GetByCodeAsync(input.Code.Value, input.CustomerId.Value, cancellationToken);

            var eventModel = orderModel is null
                ? null
                : await _eventRepository.GetByIdAsync(orderModel.ProducerId, orderModel.EventId, cancellationToken);

            // A cobrança mais recente acompanha o pedido (REQ7): é com ela que o checkout mostra o Pix e acompanha o pagamento.
            PaymentModel? paymentModel = null;

            if (orderModel is not null && eventModel is not null)
                paymentModel = await _paymentRepository.GetLatestByOrderAsync(orderModel.Id, cancellationToken);

            // Os ingressos só existem com o pedido pago (REQ8): a lista sai vazia antes disso.
            IReadOnlyList<TicketListItemModel> ticketModels = orderModel is not null && orderModel.Status == "PAID"
                ? await _ticketRepository.ListByOrderAsync(orderModel.Id, cancellationToken)
                : [];

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            // Pedido sem evento não existe: a FK composta garante o evento do pedido.
            if (orderModel is null || eventModel is null)
                return NotFound();

            var order = OrderConversions.ToOrder(orderModel);
            var tickets = ticketModels.Select(ticket => new OrderTicketOutput(ticket.Code, ticket.BatchName, ticket.HolderName, ticket.Status)).ToArray();
            var output = OrderConversions.ToOutput(order, eventModel, now, paymentModel is null ? null : OrderPaymentConversions.ToOutput(OrderPaymentConversions.ToPayment(paymentModel), now), tickets);

            RecordOutcome(GetCustomerOrderDiagnostics.OUTCOME_SERVED);

            GetCustomerOrderLog.Served(_logger, output.Code, output.Status);

            return MethodResult<OrderOutput>.Success(output);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<OrderOutput>.Error([Notification.Error(GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_CODE, GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_MESSAGE)]);

            GetCustomerOrderLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetCustomerOrderDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private MethodResult<OrderOutput> NotFound()
    {
        RecordOutcome(GetCustomerOrderDiagnostics.OUTCOME_ORDER_NOT_FOUND);

        GetCustomerOrderLog.NotFound(_logger);

        return MethodResult<OrderOutput>.Error([Notification.Error(ORDER_NOT_FOUND_NOTIFICATION_CODE, ORDER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
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
            GetCustomerOrderLog.RollbackFailed(_logger, ex);
        }
    }
}
