using Application.UseCases.Base.Events.Outputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.BoundedContexts.EventContext.Services;
using Infra.PostgreSql.Repositories.Events.Models;

namespace Application.UseCases.Base.Events;

/// <summary>Lote visível na vitrine, com a disponibilidade calculada pelo domínio.</summary>
public sealed record PublicBatchAvailability(TicketBatch Batch, TicketBatchAvailabilityResult Availability);

/// <summary>Monta a vitrine (REQ11) a partir dos eventos, dos lotes e das vendas, com as regras de disponibilidade do domínio.</summary>
public static class PublicEventConversions
{
    private static readonly PublicProducerOutput UNKNOWN_PRODUCER = new(Slug: string.Empty, Name: string.Empty);

    /// <summary>Lotes visíveis do evento (sem os pausados, RN3), com o estado de venda.</summary>
    public static IReadOnlyList<PublicBatchAvailability> Evaluate(IEnumerable<TicketBatchModel> batches, IReadOnlyDictionary<Guid, TicketBatchSalesModel> sales, DateTime now)
        => batches
            .Select(TicketBatchConversions.ToBatch)
            .Select(batch =>
            {
                var batchSales = sales.GetValueOrDefault(batch.Id.Value, TicketBatchConversions.NO_SALES);

                return new PublicBatchAvailability(batch, TicketBatchAvailability.Calculate(batch, batchSales.Sold, batchSales.Reserved, now));
            })
            .Where(item => item.Availability.SaleState != BatchSaleState.PAUSED)
            .ToArray();

    public static EventSaleSummaryResult Summarize(IReadOnlyList<PublicBatchAvailability> batches)
        => EventSaleSummary.Summarize(batches.Select(item => (item.Batch, item.Availability.SaleState)).ToArray());

    public static PublicProducerOutput ToProducer(Guid producerId, IReadOnlyDictionary<Guid, PublicProducerOutput> producers)
        => producers.GetValueOrDefault(producerId, UNKNOWN_PRODUCER);

    public static PublicEventSummaryOutput ToSummary(EventModel @event, PublicProducerOutput producer, EventSaleSummaryResult summary)
        => new(
            Slug: @event.Slug,
            Title: @event.Title,
            Category: @event.Category,
            StartsAt: DateTime.SpecifyKind(@event.StartsAt, DateTimeKind.Utc),
            VenueName: @event.VenueName,
            City: @event.City,
            State: @event.State,
            ImageUrl: @event.ImageUrl,
            Producer: producer,
            PriceFromCents: summary.PriceFromCents,
            SaleState: summary.SaleState.ToString());

    /// <summary>RN5: <c>maxSelectable</c> é o mínimo entre o disponível e o limite por pedido, e zero fora de venda.</summary>
    public static PublicBatchOutput ToBatch(PublicBatchAvailability item, int maxTicketsPerOrder)
        => new(
            Id: item.Batch.Id.Value,
            Name: item.Batch.Details.Name.Value,
            Note: item.Batch.Details.Note.Value,
            PriceCents: item.Batch.Details.Price.Cents,
            SalesStartsAt: item.Batch.Details.SalesPeriod.StartsAt,
            SalesEndsAt: item.Batch.Details.SalesPeriod.EndsAt,
            SaleState: item.Availability.SaleState.ToString(),
            MaxSelectable: item.Availability.SaleState == BatchSaleState.ON_SALE ? Math.Min(item.Availability.Available, maxTicketsPerOrder) : 0);

    public static PublicEventOutput ToDetail(EventModel @event, PublicProducerOutput producer, IReadOnlyList<PublicBatchAvailability> batches, int maxTicketsPerOrder)
        => new(
            Slug: @event.Slug,
            Title: @event.Title,
            Description: @event.Description,
            Category: @event.Category,
            StartsAt: DateTime.SpecifyKind(@event.StartsAt, DateTimeKind.Utc),
            EndsAt: DateTime.SpecifyKind(@event.EndsAt, DateTimeKind.Utc),
            DoorsOpenAt: @event.DoorsOpenAt is null ? null : DateTime.SpecifyKind(@event.DoorsOpenAt.Value, DateTimeKind.Utc),
            VenueName: @event.VenueName,
            Address: @event.Address,
            City: @event.City,
            State: @event.State,
            AgeRating: @event.AgeRating,
            ImageUrl: @event.ImageUrl,
            Producer: producer,
            SaleState: Summarize(batches).SaleState.ToString(),
            MaxTicketsPerOrder: maxTicketsPerOrder,
            Batches: batches.Select(item => ToBatch(item, maxTicketsPerOrder)).ToArray());
}
