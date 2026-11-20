using Application.UseCases.Base.Orders;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Services;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace UnitTests.Support;

/// <summary>Dados do pedido (REQ6) para os testes unitários, sobre o evento e o lote de <see cref="EventTestData"/> e <see cref="TicketBatchTestData"/>.</summary>
public static class OrderTestData
{
    /// <summary>Dentro da janela de venda do lote de <see cref="TicketBatchTestData"/>.</summary>
    public static readonly DateTime NOW = TicketBatchTestData.SALES_STARTS_AT.AddDays(1);

    public static readonly Guid CUSTOMER_ID = Guid.Parse("1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    public static readonly Guid VIP_BATCH_ID = Guid.Parse("9f0a1b2c-3d4e-4f5a-8b6c-7d8e9f0a1b2c");

    public static readonly IdValueObject CORRELATION_ID = IdValueObject.Build(EventTestData.CORRELATION_ID.ToString());

    public static OrderBatchOffer Offer(TicketBatchDetails? details = null, Guid? id = null, int sold = 0, int reserved = 0, DateTime? now = null)
    {
        var batch = TicketBatchTestData.NewBatch(details) with { Id = IdValueObject.Build((id ?? TicketBatchTestData.BATCH_ID).ToString()) };

        return new OrderBatchOffer(batch, TicketBatchAvailability.Calculate(batch, sold, reserved, now ?? NOW));
    }

    public static OrderBatchOffer VipOffer(int sold = 0, int reserved = 0)
        => Offer(TicketBatchTestData.Details(name: "VIP", priceCents: 15000, quantity: 50), VIP_BATCH_ID, sold, reserved);

    public static OrderItemRequest Request(int quantity, Guid? batchId = null)
        => new(batchId ?? TicketBatchTestData.BATCH_ID, OrderItemQuantityValueObject.Build(quantity));

    public static Domain.CrossCutting.MethodResultContext.MethodResult<Order> Create(
        IReadOnlyList<OrderItemRequest> requested,
        IReadOnlyList<OrderBatchOffer>? offers = null,
        int maxTicketsPerOrder = 6,
        DateTime? now = null)
        => Order.Create(
            @event: EventTestData.NewEvent(),
            customerId: IdValueObject.Build(CUSTOMER_ID.ToString()),
            salesChannel: SalesChannelValueObject.Build("HUB"),
            requested: requested,
            offers: offers ?? [Offer()],
            code: OrderCodeValueObject.Build("NX7K2QH9MP"),
            maxTicketsPerOrder: maxTicketsPerOrder,
            reservationMinutes: 15,
            now: now ?? NOW,
            correlationId: CORRELATION_ID);

    public static Order NewOrder(int quantity = 2)
        => Create([Request(quantity)]).Output!;

    public static OrderModel NewModel(int quantity = 2)
        => OrderConversions.ToModel(NewOrder(quantity)) with { ProducerId = EventTestData.PRODUCER_ID, EventId = EventTestData.EVENT_ID };

    public static EventModel PublishedEventModel()
        => EventTestData.NewModel() with { Status = "PUBLISHED" };
}
