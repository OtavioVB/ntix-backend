using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

public sealed record EmailVerification
{
    public static readonly TimeSpan VALIDITY = TimeSpan.FromHours(24);

    public EmailVerification(IdValueObject id, IdValueObject producerUserId, EmailVerificationTokenValueObject token, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        ProducerUserId = producerUserId;
        Token = token;
        ExpiresAt = DateTimeValueObject.Build(createdAt.Value.Add(VALIDITY));
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public IdValueObject ProducerUserId { get; set; }
    public EmailVerificationTokenValueObject Token { get; set; }
    public DateTimeValueObject ExpiresAt { get; set; }
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
