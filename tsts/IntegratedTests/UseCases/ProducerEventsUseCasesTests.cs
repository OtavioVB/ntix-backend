using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.GetProducerEvent.Inputs;
using Application.UseCases.GetProducerEvents.Inputs;
using Application.UseCases.GetProducerEvents.Outputs;
using Application.UseCases.UpdateEvent.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Eventos do produtor (REQ9) contra o PostgreSQL real: slug global, isolamento e edição.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class ProducerEventsUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id_event;";

    private static readonly DateTime STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private readonly PostgreSqlFixture _fixture;

    public ProducerEventsUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private async Task<Domain.CrossCutting.MethodResultContext.MethodResult<TOutput>> ExecuteAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<TInput, TOutput>>()
            .ExecuteAsync(input, ProducerTestData.NewAuditableInfo());
    }

    private Task<Domain.CrossCutting.MethodResultContext.MethodResult<ProducerEventOutput>> CreateEventAsync(string producerSlug, string title, DateTime? startsAt = null)
        => ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            producerSlug: producerSlug,
            title: title,
            description: "Três palcos, um pôr do sol e nenhuma pressa.",
            category: "FESTIVALS",
            startsAt: Iso(startsAt ?? STARTS_AT),
            endsAt: Iso((startsAt ?? STARTS_AT).AddHours(6)),
            doorsOpenAt: Iso((startsAt ?? STARTS_AT).AddHours(-1)),
            venueName: "Parque das Dunas",
            address: "Av. Beira-Mar, 1200",
            city: "Recife",
            state: "PE",
            ageRating: "AGE_16",
            imageUrl: "https://cdn.exemplo.com/horizonte.jpg"));

    private Task<Domain.CrossCutting.MethodResultContext.MethodResult<ProducerEventOutput>> UpdateEventAsync(string producerSlug, Guid eventId, string title)
        => ExecuteAsync<UpdateEventUseCaseInput, ProducerEventOutput>(UpdateEventUseCaseInput.Create(
            producerSlug: producerSlug,
            eventId: eventId.ToString(),
            title: title,
            description: null,
            category: "SHOWS",
            startsAt: Iso(STARTS_AT),
            endsAt: Iso(STARTS_AT.AddHours(4)),
            doorsOpenAt: null,
            venueName: "Teatro Municipal",
            address: "Praça da República, s/n",
            city: "São Paulo",
            state: "SP",
            ageRating: "FREE",
            imageUrl: null));

    // Título único por teste: o slug é global e os testes rodam no mesmo banco.
    private static string UniqueTitle()
        => $"Festival {Guid.NewGuid():N}"[..30];

    [Fact]
    public async Task Given_NewEvent_When_Creating_Should_PersistDraftAndReadItBack()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var title = UniqueTitle();

        // Act
        var created = await CreateEventAsync(producer.Slug, title);

        // Assert
        Assert.True(created.IsSuccess);
        var read = await ExecuteAsync<GetProducerEventUseCaseInput, ProducerEventOutput>(GetProducerEventUseCaseInput.Create(producer.Slug, created.Output!.Id.ToString()));

        Assert.True(read.IsSuccess);
        Assert.Equal("DRAFT", read.Output!.Status);
        Assert.Equal(title, read.Output.Title);
        Assert.Equal(title.ToLowerInvariant().Replace(' ', '-'), read.Output.Slug);
        Assert.Equal(STARTS_AT, read.Output.StartsAt);
        Assert.Equal(STARTS_AT.AddHours(-1), read.Output.DoorsOpenAt);
        Assert.Equal("PE", read.Output.State);
    }

    [Fact]
    public async Task Given_TitleUsedByAnotherProducer_When_Creating_Should_UseRandomSlugVersion()
    {
        // Arrange
        var first = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var second = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var title = UniqueTitle();
        var original = await CreateEventAsync(first.Slug, title);

        // Act
        var duplicated = await CreateEventAsync(second.Slug, title);

        // Assert
        Assert.True(duplicated.IsSuccess);
        Assert.StartsWith(original.Output!.Slug + "-", duplicated.Output!.Slug);
        Assert.Equal(original.Output.Slug.Length + 6, duplicated.Output.Slug.Length);
    }

    [Fact]
    public async Task Given_SlugAlreadyInTable_When_RepositoryCreates_Should_ReturnConflictAndKeepTransactionUsable()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var existing = (await CreateEventAsync(producer.Slug, UniqueTitle())).Output!;

        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();

        await unitOfWork.BeginTransactionAsync();
        var model = (await repository.GetByIdAsync(producer.Id, existing.Id))!;

        // Act
        var conflict = await repository.CreateAsync(model with { Id = Guid.NewGuid() });
        var retry = await repository.CreateAsync(model with { Id = Guid.NewGuid(), Slug = model.Slug + "-x1y2z" });
        await unitOfWork.RollbackAsync();

        // Assert
        Assert.Equal(EventCreationResult.SlugAlreadyExists, conflict);
        Assert.Equal(EventCreationResult.Created, retry);
    }

    [Fact]
    public async Task Given_EventOfAnotherProducer_When_ReadingOrUpdating_Should_ReportNotFound()
    {
        // Arrange
        var owner = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var intruder = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var created = (await CreateEventAsync(owner.Slug, UniqueTitle())).Output!;

        // Act
        var read = await ExecuteAsync<GetProducerEventUseCaseInput, ProducerEventOutput>(GetProducerEventUseCaseInput.Create(intruder.Slug, created.Id.ToString()));
        var updated = await UpdateEventAsync(intruder.Slug, created.Id, "Sequestro de evento");

        // Assert
        Assert.Equal("EVENT_NOT_FOUND", Assert.Single(read.Notifications).Code);
        Assert.Equal("EVENT_NOT_FOUND", Assert.Single(updated.Notifications).Code);

        var unchanged = await ExecuteAsync<GetProducerEventUseCaseInput, ProducerEventOutput>(GetProducerEventUseCaseInput.Create(owner.Slug, created.Id.ToString()));
        Assert.Equal(created.Title, unchanged.Output!.Title);
    }

    [Fact]
    public async Task Given_ExistingEvent_When_Updating_Should_ReplaceFieldsAndKeepSlug()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var created = (await CreateEventAsync(producer.Slug, UniqueTitle())).Output!;

        // Act
        var updated = await UpdateEventAsync(producer.Slug, created.Id, "Novo título do evento");

        // Assert
        Assert.True(updated.IsSuccess);
        var read = (await ExecuteAsync<GetProducerEventUseCaseInput, ProducerEventOutput>(GetProducerEventUseCaseInput.Create(producer.Slug, created.Id.ToString()))).Output!;
        Assert.Equal("Novo título do evento", read.Title);
        Assert.Equal(created.Slug, read.Slug);
        Assert.Equal("SP", read.State);
        Assert.Null(read.Description);
        Assert.Null(read.ImageUrl);
        Assert.Null(read.DoorsOpenAt);
        Assert.Equal(created.CreatedAt, read.CreatedAt);
        Assert.True(read.UpdatedAt >= created.UpdatedAt);
    }

    [Fact]
    public async Task Given_EventsInBothStatuses_When_ListingByStatus_Should_FilterAndSortByStart()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var later = (await CreateEventAsync(producer.Slug, UniqueTitle(), STARTS_AT.AddDays(10))).Output!;
        var sooner = (await CreateEventAsync(producer.Slug, UniqueTitle(), STARTS_AT)).Output!;
        var published = (await CreateEventAsync(producer.Slug, UniqueTitle(), STARTS_AT.AddDays(5))).Output!;
        await _fixture.ExecuteAsync(PUBLISH_SQL, new NpgsqlParameter("@id_event", NpgsqlDbType.Uuid) { Value = published.Id });

        // Act
        var all = await ExecuteAsync<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput>(GetProducerEventsUseCaseInput.Create(producer.Slug, null));
        var drafts = await ExecuteAsync<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput>(GetProducerEventsUseCaseInput.Create(producer.Slug, "DRAFT"));

        // Assert
        Assert.Equal([sooner.Id, published.Id, later.Id], all.Output!.Events.Select(@event => @event.Id));
        Assert.Equal([sooner.Id, later.Id], drafts.Output!.Events.Select(@event => @event.Id));
    }
}
