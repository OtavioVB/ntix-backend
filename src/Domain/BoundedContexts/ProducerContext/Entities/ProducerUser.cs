using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

public sealed record ProducerUser
{
    public ProducerUser(IdValueObject id, IdValueObject producerId, FullNameValueObject name, EmailValueObject email, PasswordHashValueObject passwordHash, PasswordSaltValueObject passwordSalt, ProducerUserType userType, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        ProducerId = producerId;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        UserType = userType;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public IdValueObject ProducerId { get; set; }
    public FullNameValueObject Name { get; set; }
    public EmailValueObject Email { get; set; }
    public PasswordHashValueObject PasswordHash { get; set; }
    public PasswordSaltValueObject PasswordSalt { get; set; }
    public ProducerUserType UserType { get; set; }
    public ProducerUserStatus Status { get; set; } = ProducerUserStatus.PENDING;
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
