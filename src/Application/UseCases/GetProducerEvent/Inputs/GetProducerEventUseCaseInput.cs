using Application.UseCases.Base.Events;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetProducerEvent.Inputs;

public sealed record GetProducerEventUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Nulo quando o id da rota não é um GUID: tratado como evento inexistente.</summary>
    public Guid? EventId { get; set; }

    private GetProducerEventUseCaseInput(SlugValueObject producerSlug, Guid? eventId)
    {
        ProducerSlug = producerSlug;
        EventId = eventId;
    }

    public static GetProducerEventUseCaseInput Create(string? producerSlug, string? eventId)
        => new(SlugValueObject.Parse(producerSlug), EventConversions.ParseEventId(eventId));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult);
}
