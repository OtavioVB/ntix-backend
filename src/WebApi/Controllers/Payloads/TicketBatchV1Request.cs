namespace WebApi.Controllers.Payloads;

/// <summary>Corpo da criação e da edição de lote (REQ10). A edição substitui todos os campos; pausar é <c>status: "INACTIVE"</c>.</summary>
/// <param name="PriceCents">Preço em centavos, de 100 (R$ 1,00) a 10.000.000.</param>
/// <param name="SalesStartsAt">ISO 8601 em UTC (ex.: 2026-12-01T12:00:00.000Z).</param>
/// <param name="Status">ACTIVE ou INACTIVE.</param>
public sealed record TicketBatchV1Request(
    string? Name,
    string? Note,
    long? PriceCents,
    int? Quantity,
    string? SalesStartsAt,
    string? SalesEndsAt,
    string? Status);
