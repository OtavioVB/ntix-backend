using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Application.UseCases.PublishEvent.Inputs;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Application.UseCases.UpdateTicketBatch.Inputs;
using Domain.BoundedContexts.EventContext.Events;
using Domain.CrossCutting.MethodResultContext;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Lotes e publicação (REQ10) contra o PostgreSQL real: preço em reais, nome único, isolamento e outbox.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class TicketBatchesAndPublicationUseCasesTests
{
    private const string SELECT_PRICE_SQL = "SELECT price FROM events.ticket_batch WHERE id_ticket_batch = @id;";
    private const string INSERT_FOREIGN_BATCH_SQL = """
        INSERT INTO events.ticket_batch (id_ticket_batch, id_event, id_producer, name, price, quantity, sales_starts_at, sales_ends_at, status,
                                         created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (gen_random_uuid(), @id_event, gen_random_uuid(), 'Intruso', 10, 1, now(), now() + interval '1 day', 'ACTIVE', now(), gen_random_uuid(), now(), gen_random_uuid());
        """;

    private static readonly DateTime STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private readonly PostgreSqlFixture _fixture;

    public TicketBatchesAndPublicationUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private async Task<MethodResult<TOutput>> ExecuteAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<TInput, TOutput>>().ExecuteAsync(input, ProducerTestData.NewAuditableInfo());
    }

    private async Task<ProducerEventOutput> CreateEventAsync(string producerSlug)
        => (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            producerSlug, $"Festival {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(STARTS_AT), Iso(STARTS_AT.AddHours(6)), null,
            "Parque das Dunas", "Av. Beira-Mar, 1200", "Recife", "PE", "AGE_16", null))).Output!;

    private Task<MethodResult<TicketBatchOutput>> CreateBatchAsync(string producerSlug, Guid eventId, string name = "1º lote - Pista", long priceCents = 12050)
        => ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            producerSlug,
            eventId.ToString(),
            TicketBatchConversions.BuildDetails(name, null, priceCents, 500, Iso(DateTime.UtcNow.AddDays(-1)), Iso(STARTS_AT), "ACTIVE")));

    private async Task GivenPixIntegrationAsync(string producerSlug)
    {
        var integration = (await ExecuteAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(CreateProducerIntegrationUseCaseInput.Create(
            producerSlug, PaymentIntegratedTestData.ABACATEPAY, "AbacatePay - conta principal", new Dictionary<string, string?> { ["API_KEY"] = "abc_dev_chave-1234" }))).Output!;

        var route = await ExecuteAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(ReplacePaymentRouteUseCaseInput.Create(producerSlug, "PIX", [integration.Id.ToString()]));

        Assert.True(route.IsSuccess);
    }

    [Fact]
    public async Task Given_NewBatch_When_Creating_Should_StorePriceInReaisAndReadItBackInCents()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(producer.Slug);

        // Act
        var created = await CreateBatchAsync(producer.Slug, @event.Id, priceCents: 12050);

        // Assert
        Assert.True(created.IsSuccess);
        Assert.Equal(120.50m, await _fixture.ScalarAsync<decimal>(SELECT_PRICE_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = created.Output!.Id }));

        var listed = await ExecuteAsync<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>(GetTicketBatchesUseCaseInput.Create(producer.Slug, @event.Id.ToString()));
        var batch = Assert.Single(listed.Output!.Batches);
        Assert.Equal(12050, batch.PriceCents);
        Assert.Equal(500, batch.AvailableQuantity);
        Assert.Equal("ON_SALE", batch.SaleState);
    }

    [Fact]
    public async Task Given_NameAlreadyUsedInEvent_When_Creating_Should_ReportConflict()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(producer.Slug);
        await CreateBatchAsync(producer.Slug, @event.Id, "VIP");

        // Act
        var duplicated = await CreateBatchAsync(producer.Slug, @event.Id, "VIP");

        // Assert
        Assert.Equal("BATCH_NAME_ALREADY_EXISTS", Assert.Single(duplicated.Notifications).Code);
    }

    [Fact]
    public async Task Given_ProducerMismatch_When_InsertingBatchDirectly_Should_BeRejectedByCompositeForeignKey()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(producer.Slug);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => _fixture.ExecuteAsync(INSERT_FOREIGN_BATCH_SQL, new NpgsqlParameter("@id_event", NpgsqlDbType.Uuid) { Value = @event.Id }));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Given_BatchOfAnotherProducer_When_Updating_Should_ReportEventNotFound()
    {
        // Arrange
        var owner = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var intruder = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(owner.Slug);
        var batch = (await CreateBatchAsync(owner.Slug, @event.Id)).Output!;

        // Act
        var result = await ExecuteAsync<UpdateTicketBatchUseCaseInput, TicketBatchOutput>(UpdateTicketBatchUseCaseInput.Create(
            intruder.Slug, @event.Id.ToString(), batch.Id.ToString(), TicketBatchConversions.BuildDetails("Sequestro", null, 100, 1, Iso(DateTime.UtcNow), Iso(STARTS_AT), "ACTIVE")));

        // Assert
        Assert.Equal("EVENT_NOT_FOUND", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_Pause_When_Updating_Should_PersistInactive()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(producer.Slug);
        var batch = (await CreateBatchAsync(producer.Slug, @event.Id)).Output!;

        // Act
        var updated = await ExecuteAsync<UpdateTicketBatchUseCaseInput, TicketBatchOutput>(UpdateTicketBatchUseCaseInput.Create(
            producer.Slug, @event.Id.ToString(), batch.Id.ToString(), TicketBatchConversions.BuildDetails(batch.Name, null, 15000, 400, Iso(DateTime.UtcNow.AddDays(-1)), Iso(STARTS_AT), "INACTIVE")));

        // Assert
        Assert.True(updated.IsSuccess);
        var listed = (await ExecuteAsync<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>(GetTicketBatchesUseCaseInput.Create(producer.Slug, @event.Id.ToString()))).Output!;
        var stored = Assert.Single(listed.Batches);
        Assert.Equal("PAUSED", stored.SaleState);
        Assert.Equal(15000, stored.PriceCents);
        Assert.Equal(400, stored.Quantity);
    }

    [Fact]
    public async Task Given_EventWithBatchAndPixIntegration_When_Publishing_Should_PersistAndWriteOutbox()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        await GivenPixIntegrationAsync(producer.Slug);
        var @event = await CreateEventAsync(producer.Slug);
        await CreateBatchAsync(producer.Slug, @event.Id);

        // Act
        var published = await ExecuteAsync<PublishEventUseCaseInput, ProducerEventOutput>(PublishEventUseCaseInput.Create(producer.Slug, @event.Id.ToString()));

        // Assert
        Assert.True(published.IsSuccess);
        Assert.Equal("PUBLISHED", published.Output!.Status);
        var outbox = Assert.Single(await _fixture.FindOutboxEventsByAggregateAsync(@event.Id));
        Assert.Equal(EventPublishedEvent.EVENT_TYPE, outbox.EventType);

        var again = await ExecuteAsync<PublishEventUseCaseInput, ProducerEventOutput>(PublishEventUseCaseInput.Create(producer.Slug, @event.Id.ToString()));
        Assert.Equal("EVENT_NOT_DRAFT", Assert.Single(again.Notifications).Code);
    }

    [Fact]
    public async Task Given_NoPixIntegration_When_Publishing_Should_KeepDraftWithoutOutbox()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await CreateEventAsync(producer.Slug);
        await CreateBatchAsync(producer.Slug, @event.Id);

        // Act
        var result = await ExecuteAsync<PublishEventUseCaseInput, ProducerEventOutput>(PublishEventUseCaseInput.Create(producer.Slug, @event.Id.ToString()));

        // Assert
        Assert.Equal("EVENT_WITHOUT_PAYMENT_METHOD", Assert.Single(result.Notifications).Code);
        Assert.Empty(await _fixture.FindOutboxEventsByAggregateAsync(@event.Id));
    }
}
