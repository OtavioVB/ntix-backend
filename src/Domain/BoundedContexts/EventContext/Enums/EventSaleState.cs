namespace Domain.BoundedContexts.EventContext.Enums;

/// <summary>Estado de venda do evento na vitrine (REQ11, RN4), derivado dos estados dos lotes visíveis.</summary>
public enum EventSaleState
{
    ON_SALE = 1,
    UPCOMING = 2,
    SOLD_OUT = 3,
    CLOSED = 4,
}
