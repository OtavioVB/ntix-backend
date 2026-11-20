using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetPublicEvent.Inputs;
using Application.UseCases.SearchPublicEvents.Inputs;
using Domain.CrossCutting.MethodResultContext;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Vitrine pública (REQ11) contra o PostgreSQL real: filtros exatos, cidade sem acento, paginação e visibilidade.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class PublicShowcaseUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    // Encerra o evento e a venda dos lotes juntos, como o tempo faria (a venda nunca termina depois do evento).
    private const string END_SQL = """
        UPDATE events.event SET starts_at = now() - interval '2 days', doors_open_at = NULL, ends_at = now() - interval '1 day' WHERE id_event = @id;
        UPDATE events.ticket_batch SET sales_starts_at = now() - interval '10 days', sales_ends_at = now() - interval '2 days' WHERE id_event = @id;
        """;

    private readonly PostgreSqlFixture _fixture;

    public PublicShowcaseUseCasesTests(PostgreSqlFixture fixture)
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

    private async Task<ProducerEventOutput> GivenEventAsync(string producerSlug, string city, string state = "PE", string category = "FESTIVALS", int inDays = 60, bool publish = true, long? batchPriceCents = 12000)
    {
        var startsAt = DateTime.UtcNow.Date.AddDays(inDays).AddHours(21);
        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            producerSlug, $"Evento {Guid.NewGuid():N}"[..30], null, category, Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", city, state, "FREE", null))).Output!;

        if (batchPriceCents is not null)
        {
            var batch = await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
                producerSlug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, batchPriceCents, 100, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")));

            Assert.True(batch.IsSuccess);
        }

        if (publish)
            await _fixture.ExecuteAsync(PUBLISH_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = created.Id });

        return created;
    }

    private Task<MethodResult<PublicEventsPageOutput>> SearchAsync(string? producer = null, string? city = null, string? state = null, string? category = null, int? page = null, int? pageSize = null)
        => ExecuteAsync<SearchPublicEventsUseCaseInput, PublicEventsPageOutput>(SearchPublicEventsUseCaseInput.Create(producer, category, city, state, null, null, page, pageSize));

    // Cidade única por teste: o banco é compartilhado e a vitrine mostra os eventos de todos os testes.
    private static string UniqueCity()
        => $"Cidade {Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Given_CityWithAccent_When_SearchingWithoutAccentAndCase_Should_FindIt()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var city = "São " + UniqueCity();
        var @event = await GivenEventAsync(producer.Slug, city);

        // Act
        var result = await SearchAsync(city: city.Replace("São", "SAO"));

        // Assert
        var item = Assert.Single(result.Output!.Items);
        Assert.Equal(@event.Slug, item.Slug);
        Assert.Equal(city, item.City);
        Assert.Equal(12000, item.PriceFromCents);
        Assert.Equal("ON_SALE", item.SaleState);
        Assert.Equal(producer.Slug, item.Producer.Slug);
    }

    [Fact]
    public async Task Given_DraftAndEndedEvents_When_Searching_Should_ListOnlyPublishedFutureOnes()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var visible = await GivenEventAsync(producer.Slug, UniqueCity());
        await GivenEventAsync(producer.Slug, UniqueCity(), publish: false);
        var ended = await GivenEventAsync(producer.Slug, UniqueCity());
        await _fixture.ExecuteAsync(END_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = ended.Id });

        // Act
        var result = await SearchAsync(producer: producer.Slug);

        // Assert
        Assert.Equal(visible.Slug, Assert.Single(result.Output!.Items).Slug);

        var endedDetail = await ExecuteAsync<GetPublicEventUseCaseInput, PublicEventOutput>(GetPublicEventUseCaseInput.Create(ended.Slug));
        Assert.True(endedDetail.IsSuccess);
        Assert.Equal("CLOSED", endedDetail.Output!.SaleState);
    }

    [Fact]
    public async Task Given_StateAndCategoryFilters_When_Searching_Should_MatchExactly()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var pe = await GivenEventAsync(producer.Slug, UniqueCity(), state: "PE", category: "SHOWS");
        await GivenEventAsync(producer.Slug, UniqueCity(), state: "SP", category: "SHOWS");
        await GivenEventAsync(producer.Slug, UniqueCity(), state: "PE", category: "THEATER");

        // Act
        var result = await SearchAsync(producer: producer.Slug, state: "PE", category: "SHOWS");

        // Assert
        Assert.Equal(pe.Slug, Assert.Single(result.Output!.Items).Slug);
    }

    [Fact]
    public async Task Given_ManyEvents_When_Paginating_Should_SortByStartAndCountTotal()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var third = await GivenEventAsync(producer.Slug, UniqueCity(), inDays: 30);
        var first = await GivenEventAsync(producer.Slug, UniqueCity(), inDays: 10);
        var second = await GivenEventAsync(producer.Slug, UniqueCity(), inDays: 20);

        // Act
        var pageOne = (await SearchAsync(producer: producer.Slug, page: 1, pageSize: 2)).Output!;
        var pageTwo = (await SearchAsync(producer: producer.Slug, page: 2, pageSize: 2)).Output!;
        var beyond = (await SearchAsync(producer: producer.Slug, page: 5, pageSize: 2)).Output!;

        // Assert
        Assert.Equal([first.Slug, second.Slug], pageOne.Items.Select(item => item.Slug));
        Assert.Equal([third.Slug], pageTwo.Items.Select(item => item.Slug));
        Assert.Equal(3, pageOne.TotalItems);
        Assert.Equal(3, pageTwo.TotalItems);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalItems);
    }

    [Fact]
    public async Task Given_DraftEvent_When_GettingBySlug_Should_ReportNotFound()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var draft = await GivenEventAsync(producer.Slug, UniqueCity(), publish: false);

        // Act
        var result = await ExecuteAsync<GetPublicEventUseCaseInput, PublicEventOutput>(GetPublicEventUseCaseInput.Create(draft.Slug));

        // Assert
        Assert.Equal("EVENT_NOT_FOUND", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_PublishedEvent_When_GettingBySlug_Should_ReturnBatchesAndProducer()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var @event = await GivenEventAsync(producer.Slug, UniqueCity(), batchPriceCents: 9900);

        // Act
        var result = await ExecuteAsync<GetPublicEventUseCaseInput, PublicEventOutput>(GetPublicEventUseCaseInput.Create(@event.Slug));

        // Assert
        Assert.True(result.IsSuccess);
        var batch = Assert.Single(result.Output!.Batches);
        Assert.Equal(9900, batch.PriceCents);
        Assert.Equal(6, batch.MaxSelectable);
        Assert.Equal("ON_SALE", batch.SaleState);
        Assert.Equal(producer.Slug, result.Output.Producer.Slug);
    }
}
