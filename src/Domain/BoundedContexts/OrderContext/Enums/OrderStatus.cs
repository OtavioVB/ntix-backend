namespace Domain.BoundedContexts.OrderContext.Enums;

/// <summary>
/// Situação gravada do pedido (REQ6). Nasce <see cref="PENDING_PAYMENT"/>; <see cref="PAID"/> vem do REQ8. <see cref="EXPIRED"/>
/// é derivado na leitura (<c>Order.GetEffectiveStatus</c>) e gravado na próxima escrita do pedido (RN12).
/// </summary>
public enum OrderStatus
{
    PENDING_PAYMENT = 1,
    PAID = 2,
    EXPIRED = 3,
}
