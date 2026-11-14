namespace Application.UseCases.GetPaymentProviders.Outputs;

public sealed record GetPaymentProvidersUseCaseOutput(IReadOnlyList<PaymentProviderOutput> Providers);

public sealed record PaymentProviderOutput(string Code, string Name, IReadOnlyList<string> PaymentMethods, IReadOnlyList<PaymentProviderCredentialKeyOutput> CredentialKeys);

public sealed record PaymentProviderCredentialKeyOutput(string Key, bool IsSecret, bool IsRequired);
