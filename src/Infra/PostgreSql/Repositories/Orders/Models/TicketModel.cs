namespace Infra.PostgreSql.Repositories.Orders.Models;

/// <summary>Linha de <c>orders.ticket</c>, com tipos primitivos (REQ8).</summary>
public sealed record TicketModel(
    Guid Id,
    Guid OrderItemId,
    Guid OrderId,
    Guid CustomerId,
    Guid EventId,
    string Code,
    string HolderName,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);

/// <summary>Ingresso com o nome do lote, para a consulta do pedido (REQ8).</summary>
public sealed record TicketListItemModel(string Code, string BatchName, string HolderName, string Status);
