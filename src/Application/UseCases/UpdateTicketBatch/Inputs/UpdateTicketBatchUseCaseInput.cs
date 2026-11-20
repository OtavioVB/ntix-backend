using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.UpdateTicketBatch.Inputs;

public sealed record UpdateTicketBatchUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Nulos quando os ids da rota não são GUID: tratados como inexistentes.</summary>
    public Guid? EventId { get; set; }

    public Guid? BatchId { get; set; }

    public TicketBatchDetails Details { get; set; }

    private UpdateTicketBatchUseCaseInput(SlugValueObject producerSlug, Guid? eventId, Guid? batchId, TicketBatchDetails details)
    {
        ProducerSlug = producerSlug;
        EventId = eventId;
        BatchId = batchId;
        Details = details;
    }

    public static UpdateTicketBatchUseCaseInput Create(string? producerSlug, string? eventId, string? batchId, TicketBatchDetails details)
        => new(SlugValueObject.Parse(producerSlug), TicketBatchConversions.ParseId(eventId), TicketBatchConversions.ParseId(batchId), details);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult, Details.GetValidation());
}
