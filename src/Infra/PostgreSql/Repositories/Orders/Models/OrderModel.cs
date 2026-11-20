namespace Infra.PostgreSql.Repositories.Orders.Models;

/// <summary>Linha de <c>orders.order_item</c>, com tipos primitivos. Preço em reais (<c>NUMERIC(18,2)</c>), congelado na criação.</summary>
public sealed record OrderItemModel(Guid Id, Guid BatchId, string BatchName, int Quantity, decimal UnitPrice);

/// <summary>Linha de <c>orders.orders</c> com seus itens, em tipos primitivos. Total em reais (<c>NUMERIC(18,2)</c>).</summary>
public sealed record OrderModel(
    Guid Id,
    string Code,
    Guid ProducerId,
    Guid EventId,
    Guid CustomerId,
    string SalesChannel,
    decimal TotalAmount,
    string Status,
    DateTime ExpiresAt,
    DateTime? PaidAt,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime UpdatedAt,
    Guid UpdatedCorrelationId,
    IReadOnlyList<OrderItemModel> Items);
