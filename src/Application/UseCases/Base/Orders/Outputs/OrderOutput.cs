namespace Application.UseCases.Base.Orders.Outputs;

/// <summary>Evento do pedido, como o checkout o mostra.</summary>
public sealed record OrderEventOutput(string Slug, string Title, DateTime StartsAt, string VenueName, string City, string State, string? ImageUrl);

/// <summary>Item do pedido, com o nome e o preço congelados na criação (REQ6, RN7).</summary>
public sealed record OrderItemOutput(Guid BatchId, string BatchName, int Quantity, long UnitPriceCents, long TotalCents);

/// <summary>Ingresso emitido para o pedido pago (REQ8). Vazio até o pagamento.</summary>
public sealed record OrderTicketOutput(string Code, string BatchName, string HolderName, string Status);

/// <summary>Pedido do comprador (REQ6), reaproveitado pelo pagamento (REQ7), pela efetivação (REQ8) e pelos pedidos do cliente (REQ13).</summary>
public sealed record OrderOutput(
    string Code,
    string Status,
    string SalesChannel,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    long TotalCents,
    OrderEventOutput Event,
    IReadOnlyList<OrderItemOutput> Items,
    IReadOnlyList<OrderTicketOutput> Tickets);
