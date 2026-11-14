namespace WebApi.Controllers.Payloads;

/// <param name="Name">Ausente mantém o nome atual.</param>
/// <param name="Status">ACTIVE ou INACTIVE; ausente mantém a situação atual.</param>
/// <param name="Credentials">Parcial: só as chaves enviadas mudam; chave com <c>null</c> é removida, se não for obrigatória.</param>
public sealed record UpdateProducerIntegrationV1Request(string? Name, string? Status, Dictionary<string, string?>? Credentials);
