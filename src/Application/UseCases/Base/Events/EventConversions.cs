using Application.UseCases.Base.Events.Outputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Application.UseCases.Base.Events;

/// <summary>Conversões entre o evento de domínio, o modelo de persistência e a saída, compartilhadas pelos casos de uso de eventos.</summary>
public static class EventConversions
{
    public static EventDetails BuildDetails(
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
            Title: EventTitleValueObject.Build(title),
            Description: EventDescriptionValueObject.Build(description),
            Category: EventCategoryValueObject.Build(category),
            Period: EventPeriodValueObject.Build(startsAt, endsAt, doorsOpenAt),
            Venue: VenueValueObject.Build(venueName, address, city, state),
            AgeRating: AgeRatingValueObject.Build(ageRating),
            ImageUrl: EventImageUrlValueObject.Build(imageUrl));

    public static EventModel ToModel(Event @event)
        => new(
            Id: @event.Id.Value,
            ProducerId: @event.ProducerId.Value,
            Title: @event.Details.Title.Value,
            Slug: @event.Slug.Value,
            Description: @event.Details.Description.Value,
            Category: @event.Details.Category.Value.ToString(),
            StartsAt: @event.Details.Period.StartsAt,
            EndsAt: @event.Details.Period.EndsAt,
            DoorsOpenAt: @event.Details.Period.DoorsOpenAt,
            VenueName: @event.Details.Venue.Name,
            Address: @event.Details.Venue.Address,
            City: @event.Details.Venue.City,
            State: @event.Details.Venue.State,
            AgeRating: @event.Details.AgeRating.Value.ToString(),
            ImageUrl: @event.Details.ImageUrl.Value,
            Status: @event.Status.ToString(),
            CreatedAt: @event.CreatedAt.Value,
            CreatedCorrelationId: @event.CreatedCorrelationId.Value,
            UpdatedAt: @event.UpdatedAt.Value,
            UpdatedCorrelationId: @event.UpdatedCorrelationId.Value);

    // O banco só guarda o que passou pela validação: os value objects são reconstruídos sem regra de relógio.
    public static Event ToEvent(EventModel model)
        => Event.Restore(
            id: IdValueObject.Build(model.Id.ToString()),
            producerId: IdValueObject.Build(model.ProducerId.ToString()),
            slug: SlugValueObject.Build(model.Slug, Event.SLUG_MAX_LENGTH),
            details: new EventDetails(
                Title: EventTitleValueObject.Build(model.Title),
                Description: EventDescriptionValueObject.Build(model.Description),
                Category: EventCategoryValueObject.Build(model.Category),
                Period: EventPeriodValueObject.Build(DateTime.SpecifyKind(model.StartsAt, DateTimeKind.Utc), DateTime.SpecifyKind(model.EndsAt, DateTimeKind.Utc), model.DoorsOpenAt is null ? null : DateTime.SpecifyKind(model.DoorsOpenAt.Value, DateTimeKind.Utc)),
                Venue: VenueValueObject.Build(model.VenueName, model.Address, model.City, model.State),
                AgeRating: AgeRatingValueObject.Build(model.AgeRating),
                ImageUrl: EventImageUrlValueObject.Build(model.ImageUrl)),
            status: Enum.Parse<EventStatus>(model.Status),
            createdAt: DateTimeValueObject.Build(model.CreatedAt),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            updatedAt: DateTimeValueObject.Build(model.UpdatedAt),
            updatedCorrelationId: IdValueObject.Build(model.UpdatedCorrelationId.ToString()));

    /// <summary>Resumo de evento sem lote e sem venda (recém-criado).</summary>
    public static readonly EventSalesSummaryModel NO_SALES = new(Capacity: 0, SoldQuantity: 0, GrossAmount: 0m, PaidOrders: 0);

    /// <summary>Saída do painel com a situação exposta em <paramref name="now"/> (REQ14, RN1) e o resumo de vendas.</summary>
    public static ProducerEventOutput ToOutput(Event @event, EventSalesSummaryModel summary, DateTime now)
        => ToOutput(ToModel(@event), @event.StatusAt(now).ToString(), summary);

    public static ProducerEventOutput ToOutput(EventModel model, EventSalesSummaryModel summary, DateTime now)
        => ToOutput(ToEvent(model), summary, now);

    public static ProducerEventSalesSummaryOutput ToSummaryOutput(EventSalesSummaryModel summary)
        => new(
            Capacity: summary.Capacity,
            SoldQuantity: summary.SoldQuantity,
            GrossCents: ToCents(summary.GrossAmount),
            PaidOrders: summary.PaidOrders);

    /// <summary>Valor em reais do banco (<c>NUMERIC(18,2)</c>) em centavos para a API.</summary>
    public static long ToCents(decimal amount)
        => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static ProducerEventOutput ToOutput(EventModel model, string status, EventSalesSummaryModel summary)
        => new(
            Id: model.Id,
            Slug: model.Slug,
            Title: model.Title,
            Description: model.Description,
            Category: model.Category,
            StartsAt: DateTime.SpecifyKind(model.StartsAt, DateTimeKind.Utc),
            EndsAt: DateTime.SpecifyKind(model.EndsAt, DateTimeKind.Utc),
            DoorsOpenAt: model.DoorsOpenAt is null ? null : DateTime.SpecifyKind(model.DoorsOpenAt.Value, DateTimeKind.Utc),
            VenueName: model.VenueName,
            Address: model.Address,
            City: model.City,
            State: model.State,
            AgeRating: model.AgeRating,
            ImageUrl: model.ImageUrl,
            Status: status,
            CreatedAt: DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc),
            UpdatedAt: DateTime.SpecifyKind(model.UpdatedAt, DateTimeKind.Utc),
            SalesSummary: ToSummaryOutput(summary));

    /// <summary>Id da rota; texto que não é um GUID vira nulo e o caso de uso responde como evento inexistente.</summary>
    public static Guid? ParseEventId(string? eventId)
        => Guid.TryParse(eventId, out var parsed) ? parsed : null;
}
