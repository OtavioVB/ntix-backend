namespace Application.UseCases.Base.Events.Outputs;

/// <summary>
/// Lote como o painel do produtor o vê (REQ10): preço em centavos, disponibilidade e estado de venda derivados.
/// <paramref name="GrossCents"/> e <paramref name="PaidOrders"/> são o detalhe de vendas do lote (REQ14), só de pedidos pagos.
/// </summary>
public sealed record TicketBatchOutput(
    Guid Id,
    string Name,
    string? Note,
    long PriceCents,
    int Quantity,
    int SoldQuantity,
    int ReservedQuantity,
    int AvailableQuantity,
    DateTime SalesStartsAt,
    DateTime SalesEndsAt,
    string Status,
    string SaleState,
    long GrossCents,
    int PaidOrders);
