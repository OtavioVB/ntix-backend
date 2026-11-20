using Domain.BoundedContexts.PaymentContext.Enums;

namespace Domain.BoundedContexts.PaymentContext.Services;

/// <summary>
/// Métodos que a Ntix já cobra de verdade, porque têm adaptador de fornecedor (hoje só Pix pelo AbacatePay, REQ7).
/// A publicação (REQ10) e o checkout (REQ7) usam a mesma lista: método sem adaptador não é oferecido, mesmo configurado.
/// </summary>
public static class PaymentMethodsWithAdapter
{
    public static readonly IReadOnlyList<PaymentMethod> ALL = [PaymentMethod.PIX];
}
