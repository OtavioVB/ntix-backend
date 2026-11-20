using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateEvent.Inputs;

public sealed record CreateEventUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    public EventDetails Details { get; set; }

    private CreateEventUseCaseInput(SlugValueObject producerSlug, EventDetails details)
    {
        ProducerSlug = producerSlug;
        Details = details;
    }

    public static CreateEventUseCaseInput Create(
        string? producerSlug,
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
            details: EventConversions.BuildDetails(title, description, category, startsAt, endsAt, doorsOpenAt, venueName, address, city, state, ageRating, imageUrl));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult, Details.GetValidation());
}
