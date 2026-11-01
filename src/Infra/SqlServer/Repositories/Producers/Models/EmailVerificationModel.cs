namespace Infra.SqlServer.Repositories.Producers.Models;

public sealed record EmailVerificationModel(Guid Id, Guid ProducerUserId, byte[] TokenHash, DateTime ExpiresAt, DateTime CreatedAt, Guid CreatedCorrelationId);
