using Application.UseCases.Base.Orders.Outputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Application.UseCases.Base.Orders;

/// <summary>Conversões do pedido entre domínio, modelo de persistência e saída, compartilhadas pelos casos de uso de pedidos.</summary>
public static class OrderConversions
{
    public static OrderModel ToModel(Order order)
        => new(
            Id: order.Id.Value,
            Code: order.Code.Value,
            ProducerId: order.ProducerId.Value,
            EventId: order.EventId.Value,
            CustomerId: order.CustomerId.Value,
            SalesChannel: order.SalesChannel.ToString(),
            TotalAmount: order.TotalCents / 100m,
            Status: order.Status.ToString(),
            ExpiresAt: order.ExpiresAt.Value,
            PaidAt: order.PaidAt,
            CreatedAt: order.CreatedAt.Value,
            CreatedCorrelationId: order.CreatedCorrelationId.Value,
            UpdatedAt: order.UpdatedAt.Value,
            UpdatedCorrelationId: order.UpdatedCorrelationId.Value,
            Items: order.Items
                .Select(item => new OrderItemModel(
                    Id: item.Id.Value,
                    BatchId: item.BatchId.Value,
                    BatchName: item.BatchName.Value,
                    Quantity: item.Quantity.Value,
                    UnitPrice: item.UnitPrice.Amount))
                .ToArray());

    // O banco só guarda o que passou pela validação: os value objects são reconstruídos sem regra de relógio.
    public static Order ToOrder(OrderModel model)
        => Order.Restore(
            id: IdValueObject.Build(model.Id.ToString()),
            code: OrderCodeValueObject.Build(model.Code),
            producerId: IdValueObject.Build(model.ProducerId.ToString()),
            eventId: IdValueObject.Build(model.EventId.ToString()),
            customerId: IdValueObject.Build(model.CustomerId.ToString()),
            salesChannel: Enum.Parse<SalesChannel>(model.SalesChannel),
            items: model.Items
                .Select(item => new OrderItem(
                    Id: IdValueObject.Build(item.Id.ToString()),
                    BatchId: IdValueObject.Build(item.BatchId.ToString()),
                    BatchName: BatchNameValueObject.Build(item.BatchName),
                    Quantity: OrderItemQuantityValueObject.Build(item.Quantity),
                    UnitPrice: TicketPriceValueObject.FromAmount(item.UnitPrice)))
                .ToArray(),
            status: Enum.Parse<OrderStatus>(model.Status),
            expiresAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.ExpiresAt, DateTimeKind.Utc)),
            paidAt: model.PaidAt is null ? null : DateTime.SpecifyKind(model.PaidAt.Value, DateTimeKind.Utc),
            createdAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc)),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            updatedAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.UpdatedAt, DateTimeKind.Utc)),
            updatedCorrelationId: IdValueObject.Build(model.UpdatedCorrelationId.ToString()));

    /// <summary>Saída do pedido. O status é o efetivo (RN12): reserva vencida aparece como EXPIRED.</summary>
    public static OrderOutput ToOutput(Order order, EventModel @event, DateTime now, OrderPaymentOutput? payment = null, IReadOnlyList<OrderTicketOutput>? tickets = null)
        => new(
            Code: order.Code.Value,
            Status: order.GetEffectiveStatus(now).ToString(),
            SalesChannel: order.SalesChannel.ToString(),
            CreatedAt: order.CreatedAt.Value,
            ExpiresAt: order.ExpiresAt.Value,
            TotalCents: order.TotalCents,
            Event: new OrderEventOutput(
                Slug: @event.Slug,
                Title: @event.Title,
                StartsAt: DateTime.SpecifyKind(@event.StartsAt, DateTimeKind.Utc),
                VenueName: @event.VenueName,
                City: @event.City,
                State: @event.State,
                ImageUrl: @event.ImageUrl),
            Items: order.Items
                .Select(item => new OrderItemOutput(
                    BatchId: item.BatchId.Value,
                    BatchName: item.BatchName.Value,
                    Quantity: item.Quantity.Value,
                    UnitPriceCents: item.UnitPrice.Cents,
                    TotalCents: item.TotalCents))
                .ToArray(),
            Tickets: tickets ?? [],
            Payment: payment,
            PaidAt: order.PaidAt);
}
