namespace Domain.BoundedContexts.EventContext.Enums;

/// <summary>Estado de venda do lote (REQ10, RN7), derivado da situação, da janela de venda e da disponibilidade. Nunca gravado.</summary>
public enum BatchSaleState
{
    PAUSED = 1,
    UPCOMING = 2,
    CLOSED = 3,
    SOLD_OUT = 4,
    ON_SALE = 5,
}
