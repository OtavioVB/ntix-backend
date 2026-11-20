using Application.UseCases.Base.Events.Outputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Services;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;

namespace Application.UseCases.Base.Events;

/// <summary>Conversões do lote entre domínio, modelo de persistência e saída, compartilhadas pelos casos de uso de lotes.</summary>
public static class TicketBatchConversions
{
    public static TicketBatchDetails BuildDetails(string? name, string? note, long? priceCents, int? quantity, string? salesStartsAt, string? salesEndsAt, string? status)
        => new(
            Name: BatchNameValueObject.Build(name),
            Note: BatchNoteValueObject.Build(note),
            Price: TicketPriceValueObject.Build(priceCents),
            Quantity: BatchQuantityValueObject.Build(quantity),
            SalesPeriod: SalesPeriodValueObject.Build(salesStartsAt, salesEndsAt),
            Status: BatchStatusValueObject.Build(status));

    public static TicketBatchModel ToModel(TicketBatch batch)
        => new(
            Id: batch.Id.Value,
            EventId: batch.EventId.Value,
            ProducerId: batch.ProducerId.Value,
            Name: batch.Details.Name.Value,
            Note: batch.Details.Note.Value,
            Price: batch.Details.Price.Amount,
            Quantity: batch.Details.Quantity.Value,
            SalesStartsAt: batch.Details.SalesPeriod.StartsAt,
            SalesEndsAt: batch.Details.SalesPeriod.EndsAt,
            Status: batch.Details.Status.Value.ToString(),
            CreatedAt: batch.CreatedAt.Value,
            CreatedCorrelationId: batch.CreatedCorrelationId.Value,
            UpdatedAt: batch.UpdatedAt.Value,
            UpdatedCorrelationId: batch.UpdatedCorrelationId.Value);

    public static TicketBatch ToBatch(TicketBatchModel model)
        => TicketBatch.Restore(
            id: IdValueObject.Build(model.Id.ToString()),
            eventId: IdValueObject.Build(model.EventId.ToString()),
            producerId: IdValueObject.Build(model.ProducerId.ToString()),
            details: new TicketBatchDetails(
                Name: BatchNameValueObject.Build(model.Name),
                Note: BatchNoteValueObject.Build(model.Note),
                Price: TicketPriceValueObject.FromAmount(model.Price),
                Quantity: BatchQuantityValueObject.Build(model.Quantity),
                SalesPeriod: SalesPeriodValueObject.Build(DateTime.SpecifyKind(model.SalesStartsAt, DateTimeKind.Utc), DateTime.SpecifyKind(model.SalesEndsAt, DateTimeKind.Utc)),
                Status: BatchStatusValueObject.Build(model.Status)),
            createdAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc)),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            updatedAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.UpdatedAt, DateTimeKind.Utc)),
            updatedCorrelationId: IdValueObject.Build(model.UpdatedCorrelationId.ToString()));

    public static TicketBatchOutput ToOutput(TicketBatch batch, TicketBatchSalesModel sales, DateTime now)
    {
        var availability = TicketBatchAvailability.Calculate(batch, sales.Sold, sales.Reserved, now);

        return new TicketBatchOutput(
            Id: batch.Id.Value,
            Name: batch.Details.Name.Value,
            Note: batch.Details.Note.Value,
            PriceCents: batch.Details.Price.Cents,
            Quantity: batch.Details.Quantity.Value,
            SoldQuantity: availability.Sold,
            ReservedQuantity: availability.Reserved,
            AvailableQuantity: availability.Available,
            SalesStartsAt: batch.Details.SalesPeriod.StartsAt,
            SalesEndsAt: batch.Details.SalesPeriod.EndsAt,
            Status: batch.Details.Status.Value.ToString(),
            SaleState: availability.SaleState.ToString());
    }

    /// <summary>Id da rota; texto que não é GUID vira nulo e o caso de uso responde como inexistente.</summary>
    public static Guid? ParseId(string? id)
        => Guid.TryParse(id, out var parsed) ? parsed : null;

    public static readonly TicketBatchSalesModel NO_SALES = new(Sold: 0, Reserved: 0);
}
