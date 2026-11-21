namespace Infra.PostgreSql.Repositories.Orders.Models;

/// <summary>Pedido do comprador na lista, com o evento e a quantidade de ingressos (REQ13). <c>Status</c> é o gravado; o efetivo sai na Application.</summary>
public sealed record CustomerOrderRowModel(
    string Code,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? PaidAt,
    decimal TotalAmount,
    int TicketCount,
    Guid ProducerId,
    string EventSlug,
    string EventTitle,
    DateTime EventStartsAt,
    string EventCity,
    string EventState,
    string? EventImageUrl);

public sealed record CustomerOrderPageModel(IReadOnlyList<CustomerOrderRowModel> Items, int TotalItems);

/// <summary>Ingresso do comprador com o evento e o lote (REQ13). Vale para a lista e para o detalhe.</summary>
public sealed record CustomerTicketRowModel(
    string Code,
    string Status,
    string BatchName,
    string HolderName,
    string OrderCode,
    DateTime IssuedAt,
    Guid ProducerId,
    string EventSlug,
    string EventTitle,
    DateTime EventStartsAt,
    DateTime? DoorsOpenAt,
    string VenueName,
    string Address,
    string City,
    string State,
    string AgeRating,
    string? EventImageUrl);

public sealed record CustomerTicketPageModel(IReadOnlyList<CustomerTicketRowModel> Items, int TotalItems);
