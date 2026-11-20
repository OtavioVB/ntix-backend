namespace Domain.BoundedContexts.PaymentContext.Enums;

/// <summary>
/// Situação gravada da cobrança de um pedido (REQ7). <see cref="PAID"/> vem da confirmação (REQ8). <see cref="EXPIRED"/>
/// é derivado na leitura e gravado antes de gerar uma nova cobrança.
/// </summary>
public enum PaymentStatus
{
    PENDING = 1,
    PAID = 2,
    EXPIRED = 3,
}
