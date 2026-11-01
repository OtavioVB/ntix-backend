using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

public sealed record Producer
{
    public Producer(IdValueObject id, ProducerNameValueObject name, EmailValueObject email, E164PhoneValueObject phone, ProducerDocumentValueObject document, SlugValueObject slug, DateTimeValueObject createdAt, IdValueObject createdCorrelationId)
    {
        Id = id;
        Name = name;
        Email = email;
        Phone = phone;
        Document = document;
        Slug = slug;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
    }

    public IdValueObject Id { get; set; }
    public ProducerNameValueObject Name { get; set; }
    public EmailValueObject Email { get; set; }
    public E164PhoneValueObject Phone { get; set; }
    public ProducerDocumentValueObject Document { get; set; }
    public SlugValueObject Slug { get; set; }
    public ProducerStatus Status { get; set; } = ProducerStatus.PENDING;
    public DateTimeValueObject CreatedAt { get; set; }
    public IdValueObject CreatedCorrelationId { get; set; }
}
