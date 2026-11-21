using Infra.PostgreSql.Repositories.Orders.Models;

namespace Infra.PostgreSql.Repositories.Orders.Interfaces;

/// <summary>Estruturas de leitura das vendas (REQ14): fatos particionados por mês e contadores por lote e por evento.</summary>
public interface ISalesRepository
{
    /// <summary>
    /// Grava os itens do pedido pago em <c>orders.sale_entry</c> e soma nos contadores do lote e do evento, na transação aberta
    /// (a mesma da efetivação). Devolve quantas linhas caíram na partição padrão, sinal de que faltam partições.
    /// </summary>
    Task<int> RegisterPaidOrderAsync(IReadOnlyList<SaleEntryModel> entries, CancellationToken cancellationToken = default);

    /// <summary>Resumo de cada evento informado da produtora; evento sem lote ou sem venda volta zerado.</summary>
    Task<IReadOnlyDictionary<Guid, EventSalesSummaryModel>> GetEventSummariesAsync(Guid producerId, IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);

    /// <summary>Vendas brutas e pedidos de cada lote informado; lote sem venda volta zerado.</summary>
    Task<IReadOnlyDictionary<Guid, TicketBatchSalesTotalsModel>> GetBatchTotalsAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken = default);

    /// <summary>Vendas por dia do evento, no intervalo semiaberto em UTC, agrupadas pelo dia no fuso informado. Dias sem venda não vêm.</summary>
    Task<IReadOnlyList<DailySalesModel>> GetDailySalesAsync(Guid eventId, DateTime fromUtc, DateTime beforeUtc, string timeZone, CancellationToken cancellationToken = default);
}
