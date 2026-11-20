using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateTicketBatch.Inputs;

public sealed record CreateTicketBatchUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Nulo quando o id da rota não é um GUID: tratado como evento inexistente.</summary>
    public Guid? EventId { get; set; }

    public TicketBatchDetails Details { get; set; }

    private CreateTicketBatchUseCaseInput(SlugValueObject producerSlug, Guid? eventId, TicketBatchDetails details)
    {
        ProducerSlug = producerSlug;
        EventId = eventId;
        Details = details;
    }

    public static CreateTicketBatchUseCaseInput Create(string? producerSlug, string? eventId, TicketBatchDetails details)
        => new(SlugValueObject.Parse(producerSlug), TicketBatchConversions.ParseId(eventId), details);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult, Details.GetValidation());
}
