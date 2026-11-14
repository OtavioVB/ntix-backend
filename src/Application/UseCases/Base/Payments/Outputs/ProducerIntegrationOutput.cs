namespace Application.UseCases.Base.Payments.Outputs;

/// <summary>Integração como o produtor a vê: credencial secreta só pelo <c>ValueHint</c> (RN10).</summary>
public sealed record ProducerIntegrationOutput(Guid Id, string ProviderCode, string Name, string Status, IReadOnlyList<ProducerIntegrationCredentialOutput> Credentials);

/// <param name="ValueHint">Últimos caracteres, só de chave secreta.</param>
/// <param name="Value">Valor em claro, só de chave não secreta.</param>
public sealed record ProducerIntegrationCredentialOutput(string Key, bool IsSecret, string? ValueHint, string? Value);
