namespace Infra.PostgreSql.Repositories.Producers.Models;

public sealed record ProducerUserSessionModel(
    Guid Id,
    Guid ProducerUserId,
    Guid FamilyId,
    byte[] RefreshTokenHash,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime? RevokedAt,
    Guid? ReplacedById);
