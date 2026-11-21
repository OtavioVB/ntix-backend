namespace Application.UseCases.Base.Events.Outputs;

/// <summary>
/// Evento como o painel do produtor o vê (REQ9). Datas em UTC; campos opcionais ausentes ficam fora do JSON.
/// <paramref name="Status"/> é a situação exposta, com <c>ENDED</c> depois do término (REQ14).
/// </summary>
public sealed record ProducerEventOutput(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    string Category,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime? DoorsOpenAt,
    string VenueName,
    string Address,
    string City,
    string State,
    string AgeRating,
    string? ImageUrl,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ProducerEventSalesSummaryOutput SalesSummary);

/// <summary>Resumo de vendas do evento (REQ14): capacidade dos lotes e vendas efetivadas (só pedidos pagos), em centavos.</summary>
public sealed record ProducerEventSalesSummaryOutput(int Capacity, int SoldQuantity, long GrossCents, int PaidOrders);
