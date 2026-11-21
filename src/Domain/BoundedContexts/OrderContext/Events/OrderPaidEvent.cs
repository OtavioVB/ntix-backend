namespace Domain.BoundedContexts.OrderContext.Events;

/// <summary>Item do pedido pago, como vai no evento (RN8).</summary>
public sealed record OrderPaidItem(Guid BatchId, int Quantity, long UnitPriceCents);

/// <summary>
/// Fato "pedido pago" para os próximos processos (carteira, extrato, e-mail). Sem nome, e-mail ou CPF do comprador (RN8).
/// </summary>
public sealed record OrderPaidEvent(
    Guid OrderId,
    string OrderCode,
    Guid ProducerId,
    Guid EventId,
    Guid CustomerId,
    string SalesChannel,
    long TotalCents,
    IReadOnlyList<OrderPaidItem> Items,
    Guid PaymentId,
    Guid PaymentIntegrationId,
    string PaymentMethod,
    DateTime PaidAt);
