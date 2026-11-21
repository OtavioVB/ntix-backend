namespace Application.UseCases.Base.Customers.Outputs;

/// <summary>Produtora do pedido ou do evento, como o comprador a vê: slug e nome comercial (REQ11, REQ13).</summary>
public sealed record CustomerProducerOutput(string Slug, string Name);

/// <summary>Evento na lista de pedidos (REQ13).</summary>
public sealed record CustomerOrderEventOutput(string Slug, string Title, DateTime StartsAt, string City, string State, string? ImageUrl);

/// <summary>Pedido na lista do comprador. <c>Status</c> é o efetivo (RN3): reserva vencida aparece como EXPIRED.</summary>
public sealed record CustomerOrderItemOutput(
    string Code,
    string Status,
    DateTime CreatedAt,
    DateTime? PaidAt,
    long TotalCents,
    int TicketCount,
    CustomerOrderEventOutput Event,
    CustomerProducerOutput Producer);

public sealed record CustomerOrdersPageOutput(IReadOnlyList<CustomerOrderItemOutput> Items, int Page, int PageSize, int TotalItems);

/// <summary>Evento do ingresso, com o local e a idade indicativa (REQ13, RN6).</summary>
public sealed record CustomerTicketEventOutput(
    string Slug,
    string Title,
    DateTime StartsAt,
    DateTime? DoorsOpenAt,
    string VenueName,
    string Address,
    string City,
    string State,
    string? ImageUrl);

/// <summary>Ingresso na lista do comprador. O <c>Code</c> é o conteúdo do QR Code (RN6).</summary>
public sealed record CustomerTicketItemOutput(string Code, string Status, string BatchName, string HolderName, string OrderCode, CustomerTicketEventOutput Event);

public sealed record CustomerTicketsPageOutput(IReadOnlyList<CustomerTicketItemOutput> Items, int Page, int PageSize, int TotalItems);

/// <summary>Detalhe do ingresso para a portaria: a lista mais a classificação indicativa e a emissão.</summary>
public sealed record CustomerTicketDetailOutput(
    string Code,
    string Status,
    string BatchName,
    string HolderName,
    string OrderCode,
    CustomerTicketEventOutput Event,
    string AgeRating,
    DateTime IssuedAt);
