using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;

namespace UnitTests.Support;

/// <summary>Dados de evento válidos para os testes do REQ9, com o relógio fixo em <see cref="NOW"/>.</summary>
public static class EventTestData
{
    public static readonly DateTime NOW = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime STARTS_AT = new(2027, 3, 20, 18, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ENDS_AT = new(2027, 3, 21, 3, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime DOORS_OPEN_AT = new(2027, 3, 20, 17, 0, 0, DateTimeKind.Utc);

    public const string PRODUCER_SLUG = "mare-cultural";
    public const string TITLE = "Festival Horizonte";
    public const string SLUG = "festival-horizonte";

    public static readonly Guid PRODUCER_ID = Guid.Parse("8f3c1d2e-4b5a-4c6d-9e7f-0a1b2c3d4e5f");
    public static readonly Guid EVENT_ID = Guid.Parse("0b6e2f1a-3c4d-4e5f-8a9b-1c2d3e4f5a6b");
    public static readonly Guid CORRELATION_ID = Guid.Parse("2c1d0e9f-8a7b-4c6d-9e5f-4a3b2c1d0e9f");

    public static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    public static EventDetails Details(
        string? title = TITLE,
        string? category = "FESTIVALS",
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        string? doorsOpenAt = null,
        string? state = "PE",
        string? imageUrl = "https://cdn.exemplo.com/horizonte.jpg",
        string? description = "Três palcos, um pôr do sol e nenhuma pressa.")
        => EventConversions.BuildDetails(
            title: title,
            description: description,
            category: category,
            startsAt: Iso(startsAt ?? STARTS_AT),
            endsAt: Iso(endsAt ?? ENDS_AT),
            doorsOpenAt: doorsOpenAt ?? Iso(DOORS_OPEN_AT),
            venueName: "Parque das Dunas",
            address: "Av. Beira-Mar, 1200",
            city: "Recife",
            state: state,
            ageRating: "AGE_16",
            imageUrl: imageUrl);

    public static Event NewEvent(DateTime? startsAt = null)
        => Event.Create(
            producerId: IdValueObject.Build(PRODUCER_ID.ToString()),
            slug: SlugValueObject.Build(SLUG, Event.SLUG_MAX_LENGTH),
            details: Details(startsAt: startsAt, endsAt: (startsAt ?? STARTS_AT).AddHours(9), doorsOpenAt: Iso((startsAt ?? STARTS_AT).AddHours(-1))),
            now: (startsAt ?? STARTS_AT).AddDays(-30),
            correlationId: IdValueObject.Build(CORRELATION_ID.ToString())).Output!;

    public static EventModel NewModel(DateTime? startsAt = null)
        => EventConversions.ToModel(NewEvent(startsAt)) with { Id = EVENT_ID };

    public static AuditableInfoValueObject AuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID.ToString(), "ntix-web", "1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "2026-11-20T12:00:00.000Z");
}
