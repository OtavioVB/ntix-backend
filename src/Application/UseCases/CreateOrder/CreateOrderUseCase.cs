using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrder.Inputs;
using Domain.BoundedContexts.EventContext.Services;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateOrder;

/// <summary>
/// Cria o pedido do cliente e reserva os ingressos (REQ6). Os lotes são travados em ordem de id antes de somar vendidos e
/// reservados, então dois pedidos pelo último ingresso nunca passam juntos (RN8). Transação curta, sem chamada externa.
/// </summary>
public sealed class CreateOrderUseCase : IUseCase<CreateOrderUseCaseInput, OrderOutput>
{
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IProducerCustomerRepository _producerCustomerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly OrderConfiguration _orderConfiguration;
    private readonly CreateOrderDiagnostics _diagnostics;
    private readonly ILogger<CreateOrderUseCase> _logger;

    public CreateOrderUseCase(
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IOrderRepository orderRepository,
        IProducerCustomerRepository producerCustomerRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        OrderConfiguration orderConfiguration,
        CreateOrderDiagnostics diagnostics,
        ILogger<CreateOrderUseCase> logger)
    {
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _orderRepository = orderRepository;
        _producerCustomerRepository = producerCustomerRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _orderConfiguration = orderConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const int EVENT_SLUG_MAX_LENGTH = 126;
    private const int MAX_CODE_ATTEMPTS = 5;

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string CREATE_ORDER_CANCELED_NOTIFICATION_CODE = "CREATE_ORDER_CANCELED";
    private const string CREATE_ORDER_CANCELED_NOTIFICATION_MESSAGE = "A criação do pedido foi cancelada.";

    public async Task<MethodResult<OrderOutput>> ExecuteAsync(CreateOrderUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateOrderDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateOrder", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return Rejected(CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED, validationResult.Notifications);

            if (input.EventSlug.Length > EVENT_SLUG_MAX_LENGTH)
                return Rejected(CreateOrderDiagnostics.OUTCOME_EVENT_NOT_FOUND, [Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);

            // O PostgreSQL guarda microssegundos: o instante é truncado para a resposta ser igual ao que fica gravado.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            var correlationId = IdValueObject.Build(auditableInfo.CorrelationId.ToString());

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var eventModel = await _eventRepository.GetPublishedBySlugAsync(input.EventSlug, cancellationToken);

            if (eventModel is null)
                return await RejectAsync(CreateOrderDiagnostics.OUTCOME_EVENT_NOT_FOUND, [Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);

            var batchIds = input.Items.Select(item => item.BatchId).ToArray();

            var lockWatch = Stopwatch.StartNew();
            var batchModels = await _ticketBatchRepository.ListByIdsForUpdateAsync(eventModel.Id, batchIds, cancellationToken);
            _diagnostics.BatchLockAcquired(lockWatch.Elapsed);

            var sales = await _ticketBatchRepository.GetSalesAsync(batchModels.Select(batch => batch.Id).ToArray(), now, cancellationToken);

            var offers = batchModels
                .Select(TicketBatchConversions.ToBatch)
                .Select(batch =>
                {
                    var batchSales = sales.GetValueOrDefault(batch.Id.Value, TicketBatchConversions.NO_SALES);

                    return new OrderBatchOffer(batch, TicketBatchAvailability.Calculate(batch, batchSales.Sold, batchSales.Reserved, now));
                })
                .ToArray();

            var orderResult = Order.Create(
                @event: EventConversions.ToEvent(eventModel),
                customerId: IdValueObject.Build(input.CustomerId.ToString()),
                salesChannel: input.SalesChannel,
                requested: input.Items,
                offers: offers,
                code: OrderCodeValueObject.Generate(),
                maxTicketsPerOrder: _orderConfiguration.MaxTicketsPerOrder,
                reservationMinutes: _orderConfiguration.ReservationMinutes,
                now: now,
                correlationId: correlationId);

            if (orderResult.IsError)
                return await RejectAsync(GetRejectionOutcome(orderResult.Notifications), orderResult.Notifications);

            var order = orderResult.Output!;

            // O código sorteado quase nunca colide; se colidir, a trava dos lotes segue valendo e basta sortear outro.
            for (var attempt = 1; await _orderRepository.CreateAsync(OrderConversions.ToModel(order), cancellationToken) == OrderCreationResult.CodeAlreadyExists; attempt++)
            {
                if (attempt == MAX_CODE_ATTEMPTS)
                    throw new InvalidOperationException("Não foi possível gerar um código de pedido único.");

                order = order with { Code = OrderCodeValueObject.Generate() };
            }

            // RN13: o primeiro pedido do cliente com o produtor cria o vínculo, na mesma transação.
            var linked = await _producerCustomerRepository.LinkAsync(eventModel.ProducerId, input.CustomerId, now, correlationId.Value, cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(CreateOrderDiagnostics.OUTCOME_CREATED);

            CreateOrderLog.Created(_logger, order.Code.Value, eventModel.Id, eventModel.ProducerId, order.TicketCount, order.TotalCents);

            if (linked)
                CreateOrderLog.CustomerLinked(_logger, eventModel.ProducerId);

            return MethodResult<OrderOutput>.Success(OrderConversions.ToOutput(order, eventModel, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<OrderOutput>.Error([Notification.Error(CREATE_ORDER_CANCELED_NOTIFICATION_CODE, CREATE_ORDER_CANCELED_NOTIFICATION_MESSAGE)]);

            CreateOrderLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateOrderDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private static string GetRejectionOutcome(IEnumerable<INotification> notifications)
    {
        var codes = notifications.Select(notification => notification.Code).ToHashSet();

        if (codes.Contains(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE))
            return CreateOrderDiagnostics.OUTCOME_NOT_ON_SALE;

        return codes.Contains(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE)
            ? CreateOrderDiagnostics.OUTCOME_INSUFFICIENT_AVAILABILITY
            : CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED;
    }

    private async Task<MethodResult<OrderOutput>> RejectAsync(string outcome, INotification[] notifications)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        return Rejected(outcome, notifications);
    }

    private MethodResult<OrderOutput> Rejected(string outcome, INotification[] notifications)
    {
        RecordOutcome(outcome);

        CreateOrderLog.Rejected(_logger, string.Join(", ", notifications.Select(notification => notification.Code)));

        return MethodResult<OrderOutput>.Error(notifications);
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
            CreateOrderLog.RollbackFailed(_logger, ex);
        }
    }
}
