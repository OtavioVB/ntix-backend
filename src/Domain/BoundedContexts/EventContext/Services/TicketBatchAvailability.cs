using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;

namespace Domain.BoundedContexts.EventContext.Services;

/// <param name="Available">Ingressos que ainda podem ser vendidos: quantidade menos vendidos e reservados, nunca negativo.</param>
public sealed record TicketBatchAvailabilityResult(int Sold, int Reserved, int Available, BatchSaleState SaleState);

/// <summary>
/// Disponibilidade e estado de venda do lote (REQ10, RN7). Vendidos são itens de pedidos pagos; reservados, de pedidos
/// aguardando pagamento dentro da reserva (REQ6). Reutilizado pela vitrine (REQ11) e pelo pedido (REQ6).
/// </summary>
public static class TicketBatchAvailability
{
    public static TicketBatchAvailabilityResult Calculate(TicketBatch batch, int sold, int reserved, DateTime now)
    {
        var available = Math.Max(0, batch.Details.Quantity.Value - sold - reserved);

        return new TicketBatchAvailabilityResult(sold, reserved, available, GetSaleState(batch, available, now));
    }

    private static BatchSaleState GetSaleState(TicketBatch batch, int available, DateTime now)
    {
        if (batch.Details.Status.Value == BatchStatus.INACTIVE)
            return BatchSaleState.PAUSED;

        if (now < batch.Details.SalesPeriod.StartsAt)
            return BatchSaleState.UPCOMING;

        if (now >= batch.Details.SalesPeriod.EndsAt)
            return BatchSaleState.CLOSED;

        return available == 0 ? BatchSaleState.SOLD_OUT : BatchSaleState.ON_SALE;
    }
}
