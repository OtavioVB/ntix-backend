namespace Infra.PostgreSql.Repositories.Producers.Models;

public sealed record ProducerUserModel(
    Guid Id,
    Guid ProducerId,
    string Name,
    string Email,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    string UserType,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);
