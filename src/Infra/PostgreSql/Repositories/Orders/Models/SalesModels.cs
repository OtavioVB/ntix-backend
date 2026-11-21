namespace Infra.PostgreSql.Repositories.Orders.Models;

/// <summary>Item de pedido pago, como vai para <c>orders.sale_entry</c> (REQ14). Preço em reais (<c>NUMERIC(18,2)</c>).</summary>
public sealed record SaleEntryModel(
    Guid OrderItemId,
    Guid OrderId,
    Guid ProducerId,
    Guid EventId,
    Guid TicketBatchId,
    int Quantity,
    decimal UnitPrice,
    DateTime PaidAt,
    Guid CorrelationId);

/// <summary>Resumo de vendas de um evento: capacidade dos lotes e vendas efetivadas. Valor em reais.</summary>
public sealed record EventSalesSummaryModel(int Capacity, int SoldQuantity, decimal GrossAmount, int PaidOrders);

/// <summary>Vendas efetivadas de um lote. Valor em reais.</summary>
public sealed record TicketBatchSalesTotalsModel(decimal GrossAmount, int PaidOrders);

/// <summary>Vendas efetivadas de um dia, no fuso do relatório. Valor em reais.</summary>
public sealed record DailySalesModel(DateOnly Date, int SoldQuantity, decimal GrossAmount, int PaidOrders);
