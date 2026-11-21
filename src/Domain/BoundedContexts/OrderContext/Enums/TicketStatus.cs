namespace Domain.BoundedContexts.OrderContext.Enums;

/// <summary>Situação do ingresso (REQ8). Nasce <see cref="VALID"/> no pagamento; <see cref="USED"/> vem do check-in (requisito futuro).</summary>
public enum TicketStatus
{
    VALID = 1,
    USED = 2,
}
