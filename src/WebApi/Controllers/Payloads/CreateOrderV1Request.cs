namespace WebApi.Controllers.Payloads;

/// <summary>Item do pedido: o lote e a quantidade. O preço não vem do corpo (REQ6, RN7).</summary>
public sealed record CreateOrderItemV1Request(string? BatchId, int? Quantity);

public sealed record CreateOrderV1Request(string? EventSlug, string? SalesChannel, IReadOnlyList<CreateOrderItemV1Request>? Items);
