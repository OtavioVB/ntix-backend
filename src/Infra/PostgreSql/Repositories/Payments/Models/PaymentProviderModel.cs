namespace Infra.PostgreSql.Repositories.Payments.Models;

public sealed record PaymentProviderModel(Guid Id, string Code, string Name, string Status, IReadOnlyList<string> PaymentMethods, IReadOnlyList<ProviderCredentialKeyModel> CredentialKeys);

public sealed record ProviderCredentialKeyModel(string Key, bool IsSecret, bool IsRequired);
