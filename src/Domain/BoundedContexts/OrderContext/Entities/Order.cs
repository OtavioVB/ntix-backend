using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.BoundedContexts.EventContext.Services;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.BoundedContexts.OrderContext.Events;
using Domain.BoundedContexts.OrderContext.Services;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.OrderContext.Entities;

/// <summary>Item pedido pelo comprador: o lote e a quantidade. O preço nunca vem do pedido (RN7).</summary>
public sealed record OrderItemRequest(Guid BatchId, OrderItemQuantityValueObject Quantity);

/// <summary>Lote do evento, travado, com a disponibilidade calculada pelo domínio (RN8).</summary>
public sealed record OrderBatchOffer(TicketBatch Batch, TicketBatchAvailabilityResult Availability);

/// <summary>
/// Pedido de ingressos de um cliente para um evento (REQ6). Nasce <see cref="OrderStatus.PENDING_PAYMENT"/>, reservando os
/// ingressos até <see cref="ExpiresAt"/>; sem pagamento até lá, a reserva some sozinha (RN9, RN12).
/// </summary>
public sealed record Order
{
    private Order(
        IdValueObject id, OrderCodeValueObject code, IdValueObject producerId, IdValueObject eventId, IdValueObject customerId,
        SalesChannel salesChannel, IReadOnlyList<OrderItem> items, OrderStatus status, DateTimeValueObject expiresAt, DateTime? paidAt,
        DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
    {
        Id = id;
        Code = code;
        ProducerId = producerId;
        EventId = eventId;
        CustomerId = customerId;
        SalesChannel = salesChannel;
        Items = items;
        Status = status;
        ExpiresAt = expiresAt;
        PaidAt = paidAt;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        UpdatedAt = updatedAt;
        UpdatedCorrelationId = updatedCorrelationId;
    }

    public IdValueObject Id { get; init; }
    public OrderCodeValueObject Code { get; init; }
    public IdValueObject ProducerId { get; init; }
    public IdValueObject EventId { get; init; }
    public IdValueObject CustomerId { get; init; }
    public SalesChannel SalesChannel { get; init; }
    public IReadOnlyList<OrderItem> Items { get; init; }
    public OrderStatus Status { get; init; }
    public DateTimeValueObject ExpiresAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject UpdatedAt { get; init; }
    public IdValueObject UpdatedCorrelationId { get; init; }

    public long TotalCents => Items.Sum(item => item.TotalCents);

    public int TicketCount => Items.Sum(item => item.Quantity.Value);

    public const string ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_CODE = "ORDER_TICKET_LIMIT_EXCEEDED";
    private const string ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_MESSAGE = "O pedido pode ter no máximo {0} ingressos.";

    public const string BATCH_NOT_FOUND_NOTIFICATION_CODE = "BATCH_NOT_FOUND";
    private const string BATCH_NOT_FOUND_NOTIFICATION_MESSAGE = "Um dos lotes do pedido não existe neste evento.";

    public const string BATCH_NOT_ON_SALE_NOTIFICATION_CODE = "BATCH_NOT_ON_SALE";
    private const string BATCH_NOT_ON_SALE_NOTIFICATION_MESSAGE = "O lote \"{0}\" não está em venda.";

    public const string BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE = "BATCH_INSUFFICIENT_AVAILABILITY";
    private const string BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_MESSAGE = "O lote \"{0}\" tem apenas {1} ingresso(s) disponível(is).";

    /// <summary>
    /// Cria o pedido reservando os ingressos (RN3 a RN9). Os lotes chegam travados, lidos do evento do pedido: o que não veio
    /// não pertence a ele (RN4). Nenhum item é reservado se algum lote falhar (RN8). Preço e nome vêm do lote (RN7).
    /// </summary>
    public static MethodResult<Order> Create(
        Event @event,
        IdValueObject customerId,
        SalesChannelValueObject salesChannel,
        IReadOnlyList<OrderItemRequest> requested,
        IReadOnlyList<OrderBatchOffer> offers,
        OrderCodeValueObject code,
        int maxTicketsPerOrder,
        int reservationMinutes,
        DateTime now,
        IdValueObject correlationId)
    {
        if (requested.Sum(item => (long)item.Quantity.Value) > maxTicketsPerOrder)
            return Failure(ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_CODE, string.Format(ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_MESSAGE, maxTicketsPerOrder));

        var selected = requested
            .Select(item => (Request: item, Offer: offers.FirstOrDefault(offer => offer.Batch.Id.Value == item.BatchId)))
            .ToArray();

        if (selected.Any(item => item.Offer is null))
            return Failure(BATCH_NOT_FOUND_NOTIFICATION_CODE, BATCH_NOT_FOUND_NOTIFICATION_MESSAGE);

        var notOnSale = selected
            .Where(item => item.Offer!.Availability.SaleState is BatchSaleState.PAUSED or BatchSaleState.UPCOMING or BatchSaleState.CLOSED)
            .Select(item => (INotification)Notification.Error(BATCH_NOT_ON_SALE_NOTIFICATION_CODE, string.Format(BATCH_NOT_ON_SALE_NOTIFICATION_MESSAGE, item.Offer!.Batch.Details.Name.Value)))
            .ToArray();

        if (notOnSale.Length > 0)
            return MethodResult<Order>.Error(notOnSale);

        var insufficient = selected
            .Where(item => item.Offer!.Availability.Available < item.Request.Quantity.Value)
            .Select(item => (INotification)Notification.Error(
                BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE,
                string.Format(BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_MESSAGE, item.Offer!.Batch.Details.Name.Value, item.Offer.Availability.Available)))
            .ToArray();

        if (insufficient.Length > 0)
            return MethodResult<Order>.Error(insufficient);

        var items = selected
            .Select(item => new OrderItem(
                Id: IdValueObject.Build(),
                BatchId: item.Offer!.Batch.Id,
                BatchName: item.Offer.Batch.Details.Name,
                Quantity: item.Request.Quantity,
                UnitPrice: item.Offer.Batch.Details.Price))
            .ToArray();

        var timestamp = DateTimeValueObject.Build(now);

        return MethodResult<Order>.Success(new Order(
            id: IdValueObject.Build(),
            code: code,
            producerId: @event.ProducerId,
            eventId: @event.Id,
            customerId: customerId,
            salesChannel: salesChannel.Value,
            items: items,
            status: OrderStatus.PENDING_PAYMENT,
            expiresAt: DateTimeValueObject.Build(now.AddMinutes(reservationMinutes)),
            paidAt: null,
            createdAt: timestamp,
            createdCorrelationId: correlationId,
            updatedAt: timestamp,
            updatedCorrelationId: correlationId));
    }

    /// <summary>Reconstrói o pedido gravado.</summary>
    public static Order Restore(
        IdValueObject id, OrderCodeValueObject code, IdValueObject producerId, IdValueObject eventId, IdValueObject customerId,
        SalesChannel salesChannel, IReadOnlyList<OrderItem> items, OrderStatus status, DateTimeValueObject expiresAt, DateTime? paidAt,
        DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
        => new(id, code, producerId, eventId, customerId, salesChannel, items, status, expiresAt, paidAt, createdAt, createdCorrelationId, updatedAt, updatedCorrelationId);

    /// <summary>
    /// RN12: pedido aguardando pagamento cuja reserva venceu aparece como <see cref="OrderStatus.EXPIRED"/>, sem rotina em segundo plano.
    /// Reutilizado pelo pagamento (REQ7), pela efetivação (REQ8) e pelos pedidos do cliente (REQ13).
    /// </summary>
    public OrderStatus GetEffectiveStatus(DateTime now)
        => EffectiveStatusOf(Status, ExpiresAt.Value, now);

    /// <summary>
    /// Status efetivo de um pedido gravado, sem carregá-lo: as listas do comprador (REQ13) filtram e mostram pelo mesmo cálculo (RN3).
    /// </summary>
    public static OrderStatus EffectiveStatusOf(OrderStatus status, DateTime expiresAt, DateTime now)
        => status == OrderStatus.PENDING_PAYMENT && expiresAt <= now ? OrderStatus.EXPIRED : status;

    /// <summary>RN12: grava o vencimento da reserva na próxima escrita do pedido.</summary>
    public Order MarkExpired(DateTime now, IdValueObject correlationId)
        => this with { Status = OrderStatus.EXPIRED, UpdatedAt = DateTimeValueObject.Build(now), UpdatedCorrelationId = correlationId };

    /// <summary>
    /// Confirma o pagamento (REQ8, RN6, RN10): o pedido vira PAID, um ingresso VALID por unidade de cada item, no nome do comprador,
    /// e o evento <c>OrderPaid</c>. Pedido vencido é honrado (RN10) e sinalizado. Quem chama já verificou que não está PAID (RN6).
    /// </summary>
    public OrderPaymentConfirmation ConfirmPayment(Payment payment, string holderName, DateTime now, IdValueObject correlationId)
    {
        var timestamp = DateTimeValueObject.Build(now);
        var paidAfterExpiration = GetEffectiveStatus(now) == OrderStatus.EXPIRED;

        var tickets = Items
            .SelectMany(item => Enumerable.Range(0, item.Quantity.Value).Select(_ => new Ticket(
                Id: IdValueObject.Build(),
                OrderItemId: item.Id,
                OrderId: Id,
                CustomerId: CustomerId,
                EventId: EventId,
                Code: TicketCodeValueObject.Generate().Value,
                HolderName: holderName,
                Status: TicketStatus.VALID,
                CreatedAt: now,
                CreatedCorrelationId: correlationId)))
            .ToArray();

        var paid = this with
        {
            Status = OrderStatus.PAID,
            PaidAt = now,
            UpdatedAt = timestamp,
            UpdatedCorrelationId = correlationId,
        };

        var @event = new OrderPaidEvent(
            OrderId: Id.Value,
            OrderCode: Code.Value,
            ProducerId: ProducerId.Value,
            EventId: EventId.Value,
            CustomerId: CustomerId.Value,
            SalesChannel: SalesChannel.ToString(),
            TotalCents: TotalCents,
            Items: Items.Select(item => new OrderPaidItem(item.BatchId.Value, item.Quantity.Value, item.UnitPrice.Cents)).ToArray(),
            PaymentId: payment.Id.Value,
            PaymentIntegrationId: payment.IntegrationId.Value,
            PaymentMethod: payment.Method.ToString(),
            PaidAt: now);

        return new OrderPaymentConfirmation(paid, tickets, @event, paidAfterExpiration);
    }

    private static MethodResult<Order> Failure(string code, string message)
        => MethodResult<Order>.Error([Notification.Error(code, message)]);
}
