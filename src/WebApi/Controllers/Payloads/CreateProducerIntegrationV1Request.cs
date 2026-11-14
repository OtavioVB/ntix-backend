namespace WebApi.Controllers.Payloads;

/// <param name="ProviderCode">Código do fornecedor no catálogo (ex.: ABACATEPAY).</param>
/// <param name="Credentials">Credenciais em chave-valor, com as chaves do catálogo do fornecedor.</param>
public sealed record CreateProducerIntegrationV1Request(string? ProviderCode, string? Name, Dictionary<string, string?>? Credentials);
