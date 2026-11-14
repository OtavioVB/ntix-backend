namespace Infra.PostgreSql.Repositories.Payments.Models;

public sealed record ProducerIntegrationModel(
    Guid Id,
    Guid ProducerId,
    Guid ProviderId,
    string Name,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime UpdatedAt,
    Guid UpdatedCorrelationId,
    IReadOnlyList<ProducerIntegrationCredentialModel> Credentials);

/// <param name="EncryptedValue">Nonce, tag e texto cifrado (AES-256-GCM). Nunca o valor em claro.</param>
/// <param name="ValueHint">Últimos caracteres do valor, só de chave secreta.</param>
public sealed record ProducerIntegrationCredentialModel(string Key, byte[] EncryptedValue, string? ValueHint, DateTime UpdatedAt, Guid UpdatedCorrelationId);
