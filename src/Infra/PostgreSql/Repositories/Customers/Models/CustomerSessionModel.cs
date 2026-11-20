namespace Infra.PostgreSql.Repositories.Customers.Models;

public sealed record CustomerSessionModel(
    Guid Id,
    Guid CustomerId,
    Guid FamilyId,
    byte[] RefreshTokenHash,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime? RevokedAt,
    Guid? ReplacedById);
