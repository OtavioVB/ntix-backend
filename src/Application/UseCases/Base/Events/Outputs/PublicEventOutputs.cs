namespace Application.UseCases.Base.Events.Outputs;

/// <summary>Produtora como a vitrine mostra: slug e nome comercial (REQ11, RN6).</summary>
public sealed record PublicProducerOutput(string Slug, string Name);

/// <summary>Item da lista do hub e da loja (REQ11). Sem estoque nem vendidos (RN5).</summary>
public sealed record PublicEventSummaryOutput(
    string Slug,
    string Title,
    string Category,
    DateTime StartsAt,
    string VenueName,
    string City,
    string State,
    string? ImageUrl,
    PublicProducerOutput Producer,
    long? PriceFromCents,
    string SaleState);

public sealed record PublicEventsPageOutput(IReadOnlyList<PublicEventSummaryOutput> Items, int Page, int PageSize, int TotalItems);

/// <param name="MaxSelectable">Mínimo entre o disponível e o limite por pedido; zero fora de venda (RN5).</param>
public sealed record PublicBatchOutput(
    Guid Id,
    string Name,
    string? Note,
    long PriceCents,
    DateTime SalesStartsAt,
    DateTime SalesEndsAt,
    string SaleState,
    int MaxSelectable);

/// <summary>Página do evento (REQ11): dados do evento, produtora e lotes visíveis (sem os pausados, RN3).</summary>
public sealed record PublicEventOutput(
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
    PublicProducerOutput Producer,
    string SaleState,
    int MaxTicketsPerOrder,
    IReadOnlyList<PublicBatchOutput> Batches);
