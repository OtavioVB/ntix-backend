using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;

namespace UnitTests.Support;

/// <summary>Lotes válidos para os testes do REQ10, sobre o evento de <see cref="EventTestData"/> (20/03/2027).</summary>
public static class TicketBatchTestData
{
    public static readonly Guid BATCH_ID = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f");

    public static readonly DateTime SALES_STARTS_AT = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime SALES_ENDS_AT = new(2027, 1, 31, 23, 59, 59, DateTimeKind.Utc);

    public static TicketBatchDetails Details(
        string? name = "1º lote - Pista",
        long? priceCents = 12000,
        int? quantity = 500,
        DateTime? salesStartsAt = null,
        DateTime? salesEndsAt = null,
        string? status = "ACTIVE",
        string? note = "Meia-entrada mediante documento")
        => TicketBatchConversions.BuildDetails(
            name: name,
            note: note,
            priceCents: priceCents,
            quantity: quantity,
            salesStartsAt: EventTestData.Iso(salesStartsAt ?? SALES_STARTS_AT),
            salesEndsAt: EventTestData.Iso(salesEndsAt ?? SALES_ENDS_AT),
            status: status);

    public static TicketBatch NewBatch(TicketBatchDetails? details = null)
        => TicketBatch.Create(EventTestData.NewEvent(), details ?? Details(), EventTestData.NOW, IdValueObject.Build(EventTestData.CORRELATION_ID.ToString())).Output!;

    public static TicketBatchModel NewModel(TicketBatchDetails? details = null)
        => TicketBatchConversions.ToModel(NewBatch(details)) with { Id = BATCH_ID, EventId = EventTestData.EVENT_ID };
}
