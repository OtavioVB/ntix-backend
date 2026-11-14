using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.PaymentContext.Entities;

/// <summary>Chave de credencial que o fornecedor exige (ex.: API_KEY). <see cref="IsSecret"/> nunca é devolvida em claro (RN10).</summary>
public sealed record ProviderCredentialKey(string Key, bool IsSecret, bool IsRequired);

/// <summary>Fornecedor de pagamento do catálogo da Ntix (RN3): que métodos aceita e que credenciais exige.</summary>
public sealed record Provider
{
    public Provider(IdValueObject id, string code, string name, ProviderStatus status, IReadOnlySet<PaymentMethod> paymentMethods, IReadOnlyList<ProviderCredentialKey> credentialKeys)
    {
        Id = id;
        Code = code;
        Name = name;
        Status = status;
        PaymentMethods = paymentMethods;
        CredentialKeys = credentialKeys;
    }

    public IdValueObject Id { get; }
    public string Code { get; }
    public string Name { get; }
    public ProviderStatus Status { get; }
    public IReadOnlySet<PaymentMethod> PaymentMethods { get; }
    public IReadOnlyList<ProviderCredentialKey> CredentialKeys { get; }

    public bool IsEnabled => Status == ProviderStatus.ENABLED;

    public bool Supports(PaymentMethod paymentMethod)
        => PaymentMethods.Contains(paymentMethod);

    /// <summary>Chave do catálogo pelo nome exato, ou nula quando o fornecedor não a conhece.</summary>
    public ProviderCredentialKey? FindCredentialKey(string key)
        => CredentialKeys.FirstOrDefault(credentialKey => credentialKey.Key == key);

    public IEnumerable<ProviderCredentialKey> RequiredCredentialKeys
        => CredentialKeys.Where(credentialKey => credentialKey.IsRequired);
}
