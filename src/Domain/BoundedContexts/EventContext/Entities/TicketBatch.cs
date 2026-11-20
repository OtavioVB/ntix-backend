using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.EventContext.Entities;

/// <summary>Campos que o produtor informa na criação e substitui na edição do lote (REQ10).</summary>
public sealed record TicketBatchDetails(
    BatchNameValueObject Name,
    BatchNoteValueObject Note,
    TicketPriceValueObject Price,
    BatchQuantityValueObject Quantity,
    SalesPeriodValueObject SalesPeriod,
    BatchStatusValueObject Status)
{
    public MethodResult GetValidation()
        => MethodResult.Factory(Name.MethodResult, Note.MethodResult, Price.MethodResult, Quantity.MethodResult, SalesPeriod.MethodResult, Status.MethodResult);
}

/// <summary>
/// Lote de ingressos de um evento (REQ10): preço, quantidade e janela de venda. Não é excluído, só pausado (RN8).
/// </summary>
public sealed record TicketBatch
{
    private TicketBatch(IdValueObject id, IdValueObject eventId, IdValueObject producerId, TicketBatchDetails details, DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
    {
        Id = id;
        EventId = eventId;
        ProducerId = producerId;
        Details = details;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        UpdatedAt = updatedAt;
        UpdatedCorrelationId = updatedCorrelationId;
    }

    public IdValueObject Id { get; init; }
    public IdValueObject EventId { get; init; }
    public IdValueObject ProducerId { get; init; }
    public TicketBatchDetails Details { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject UpdatedAt { get; init; }
    public IdValueObject UpdatedCorrelationId { get; init; }

    private const string BATCH_ENDS_AFTER_EVENT_NOTIFICATION_MESSAGE = "A venda do lote deve terminar até o fim do evento.";

    public const string BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_CODE = "BATCH_QUANTITY_BELOW_SOLD";
    private const string BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_MESSAGE = "A quantidade do lote não pode ficar abaixo dos ingressos já vendidos e reservados.";

    /// <summary>Cria o lote do evento. RN5: a venda termina até o fim do evento. RN9: evento iniciado não ganha lote.</summary>
    public static MethodResult<TicketBatch> Create(Event @event, TicketBatchDetails details, DateTime now, IdValueObject correlationId)
    {
        var validation = Validate(@event, details, now);

        if (validation.IsError)
            return MethodResult<TicketBatch>.Error(validation.Notifications);

        return MethodResult<TicketBatch>.Success(new TicketBatch(
            id: IdValueObject.Build(),
            eventId: @event.Id,
            producerId: @event.ProducerId,
            details: details,
            createdAt: DateTimeValueObject.Build(now),
            createdCorrelationId: correlationId,
            updatedAt: DateTimeValueObject.Build(now),
            updatedCorrelationId: correlationId));
    }

    /// <summary>Reconstrói o lote gravado, sem revalidar regras que dependem do relógio ou do evento.</summary>
    public static TicketBatch Restore(IdValueObject id, IdValueObject eventId, IdValueObject producerId, TicketBatchDetails details, DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
        => new(id, eventId, producerId, details, createdAt, createdCorrelationId, updatedAt, updatedCorrelationId);

    /// <summary>Substitui os campos do lote. RN6: a quantidade não fica abaixo de vendidos e reservados.</summary>
    public MethodResult<TicketBatch> Update(Event @event, TicketBatchDetails details, int soldAndReserved, DateTime now, IdValueObject correlationId)
    {
        var validation = Validate(@event, details, now);

        if (validation.IsError)
            return MethodResult<TicketBatch>.Error(validation.Notifications);

        if (details.Quantity.Value < soldAndReserved)
            return MethodResult<TicketBatch>.Error([Notification.Error(BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_CODE, BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_MESSAGE)]);

        return MethodResult<TicketBatch>.Success(this with { Details = details, UpdatedAt = DateTimeValueObject.Build(now), UpdatedCorrelationId = correlationId });
    }

    private static MethodResult Validate(Event @event, TicketBatchDetails details, DateTime now)
    {
        if (!@event.CanBeEdited(now))
            return MethodResult.Error([Notification.Error(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE, Event.EVENT_ALREADY_STARTED_NOTIFICATION_MESSAGE)]);

        var validation = details.GetValidation();

        if (validation.IsError)
            return validation;

        if (details.SalesPeriod.EndsAt > @event.Details.Period.EndsAt)
            return MethodResult.Error([Notification.Error(SalesPeriodValueObject.BATCH_SALES_PERIOD_INVALID_NOTIFICATION_CODE, BATCH_ENDS_AFTER_EVENT_NOTIFICATION_MESSAGE)]);

        return MethodResult.Success();
    }
}
