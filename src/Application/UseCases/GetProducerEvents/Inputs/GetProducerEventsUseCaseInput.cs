using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetProducerEvents.Inputs;

public sealed record GetProducerEventsUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Filtro opcional; nulo traz todos os eventos.</summary>
    public EventStatusValueObject? Status { get; set; }

    private GetProducerEventsUseCaseInput(SlugValueObject producerSlug, EventStatusValueObject? status)
    {
        ProducerSlug = producerSlug;
        Status = status;
    }

    public static GetProducerEventsUseCaseInput Create(string? producerSlug, string? status)
        => new(SlugValueObject.Parse(producerSlug), string.IsNullOrEmpty(status) ? (EventStatusValueObject?)null : EventStatusValueObject.Build(status));

    public MethodResult GetInputValidation()
        => Status is null
            ? MethodResult.Factory(ProducerSlug.MethodResult)
            : MethodResult.Factory(ProducerSlug.MethodResult, Status.Value.MethodResult);
}
