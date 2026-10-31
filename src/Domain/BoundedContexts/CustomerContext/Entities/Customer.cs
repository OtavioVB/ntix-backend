using Domain.ValueObjects;

namespace Domain.BoundedContexts.CustomerContext.Entities;

public sealed record Customer
{
    public Customer(IdValueObject id, FullNameValueObject fullName, EmailValueObject email, CpfValueObject document, SlugValueObject slug, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        FullName = fullName;
        Email = email;
        Document = document;
        Slug = slug;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public FullNameValueObject FullName { get; set; }
    public EmailValueObject Email { get; set; }
    public CpfValueObject Document { get; set; }
    public SlugValueObject Slug { get; set; }
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
