using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

public sealed record Producer
{
    public Producer(IdValueObject id, ProducerNameValueObject name, EmailValueObject email, PhoneValueObject phone, SlugValueObject slug, ProducerStatus status, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        Name = name;
        Email = email;
        Phone = phone;
        Slug = slug;
        Status = status;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public ProducerNameValueObject Name { get; set; }
    public EmailValueObject Email { get; set; }
    public PhoneValueObject Phone { get; set; }
    public SlugValueObject Slug { get; set; }
    public ProducerStatus Status { get; set; }
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
