using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.UpdateEvent.Inputs;

public sealed record UpdateEventUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Nulo quando o id da rota não é um GUID: tratado como evento inexistente.</summary>
    public Guid? EventId { get; set; }

    public EventDetails Details { get; set; }

    private UpdateEventUseCaseInput(SlugValueObject producerSlug, Guid? eventId, EventDetails details)
    {
        ProducerSlug = producerSlug;
        EventId = eventId;
        Details = details;
    }

    public static UpdateEventUseCaseInput Create(
        string? producerSlug,
        string? eventId,
        string? title,
        string? description,
        string? category,
        string? startsAt,
        string? endsAt,
        string? doorsOpenAt,
        string? venueName,
        string? address,
        string? city,
        string? state,
        string? ageRating,
        string? imageUrl)
        => new(
            producerSlug: SlugValueObject.Parse(producerSlug),
            eventId: EventConversions.ParseEventId(eventId),
            details: EventConversions.BuildDetails(title, description, category, startsAt, endsAt, doorsOpenAt, venueName, address, city, state, ageRating, imageUrl));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult, Details.GetValidation());
}
