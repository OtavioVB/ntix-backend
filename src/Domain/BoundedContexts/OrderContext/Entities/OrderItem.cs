using Domain.ValueObjects;

namespace Domain.BoundedContexts.OrderContext.Entities;

/// <summary>Item do pedido (REQ6): lote, quantidade e, congelados na criação, o nome do lote e o preço unitário (RN7).</summary>
public sealed record OrderItem(
    IdValueObject Id,
    IdValueObject BatchId,
    BatchNameValueObject BatchName,
    OrderItemQuantityValueObject Quantity,
    TicketPriceValueObject UnitPrice)
{
    public long TotalCents => Quantity.Value * UnitPrice.Cents;
}
