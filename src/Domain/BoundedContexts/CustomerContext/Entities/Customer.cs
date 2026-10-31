using Domain.ValueObjects;

namespace Domain.BoundedContexts.CustomerContext.Entities;

public sealed record Customer
{
    public Customer(IdValueObject id, FullNameValueObject fullName, EmailValueObject email, CpfValueObject document, SlugValueObject slug, PasswordHashValueObject passwordHash, PasswordSaltValueObject passwordSalt, PhoneNumberValueObject phoneNumber, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        FullName = fullName;
        Email = email;
        Document = document;
        Slug = slug;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        PhoneNumber = phoneNumber;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public FullNameValueObject FullName { get; set; }
    public EmailValueObject Email { get; set; }
    public CpfValueObject Document { get; set; }
    public SlugValueObject Slug { get; set; }
    public PasswordHashValueObject PasswordHash { get; set; }
    public PasswordSaltValueObject PasswordSalt { get; set; }
    public PhoneNumberValueObject PhoneNumber { get; set; }
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
