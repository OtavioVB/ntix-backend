using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;

namespace Domain.BoundedContexts.EventContext.Services;

/// <param name="PriceFromCents">Menor preço entre os lotes em venda; sem nenhum, entre os que vão abrir. Nulo sem nenhum dos dois.</param>
public sealed record EventSaleSummaryResult(EventSaleState SaleState, long? PriceFromCents);

/// <summary>
/// Resumo de venda do evento para a vitrine (REQ11, RN4): <c>ON_SALE</c> se algum lote está em venda; senão
/// <c>UPCOMING</c> se algum vai abrir; senão <c>SOLD_OUT</c> se algum esgotou; senão <c>CLOSED</c>. Lote pausado não conta.
/// </summary>
public static class EventSaleSummary
{
    public static EventSaleSummaryResult Summarize(IReadOnlyList<(TicketBatch Batch, BatchSaleState SaleState)> batches)
    {
        var visible = batches.Where(item => item.SaleState != BatchSaleState.PAUSED).ToArray();

        long? MinPrice(BatchSaleState state)
        {
            var prices = visible.Where(item => item.SaleState == state).Select(item => item.Batch.Details.Price.Cents).ToArray();

            return prices.Length == 0 ? null : prices.Min();
        }

        if (visible.Any(item => item.SaleState == BatchSaleState.ON_SALE))
            return new EventSaleSummaryResult(EventSaleState.ON_SALE, MinPrice(BatchSaleState.ON_SALE));

        if (visible.Any(item => item.SaleState == BatchSaleState.UPCOMING))
            return new EventSaleSummaryResult(EventSaleState.UPCOMING, MinPrice(BatchSaleState.UPCOMING));

        if (visible.Any(item => item.SaleState == BatchSaleState.SOLD_OUT))
            return new EventSaleSummaryResult(EventSaleState.SOLD_OUT, null);

        return new EventSaleSummaryResult(EventSaleState.CLOSED, null);
    }
}
