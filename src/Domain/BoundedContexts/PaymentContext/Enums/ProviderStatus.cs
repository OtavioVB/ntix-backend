namespace Domain.BoundedContexts.PaymentContext.Enums;

/// <summary>Situação do fornecedor no catálogo da Ntix. <see cref="DISABLED"/> sai do ranqueamento de todos os produtores.</summary>
public enum ProviderStatus
{
    ENABLED = 1,
    DISABLED = 2,
}
