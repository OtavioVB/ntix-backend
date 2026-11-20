using Application.UseCases.Base.Configuration;
using Application.UseCases.GetPublicEvent;
using Application.UseCases.GetPublicEvent.Inputs;
using Application.UseCases.SearchPublicEvents;
using Application.UseCases.SearchPublicEvents.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class PublicShowcaseUseCasesTests : IDisposable
{
    private static readonly DateTime NOW = TicketBatchTestData.SALES_STARTS_AT.AddDays(1);

    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ITicketBatchRepository _batchRepository = Substitute.For<ITicketBatchRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly SearchPublicEventsUseCase _searchUseCase;
    private readonly GetPublicEventUseCase _getUseCase;

    private EventSearchModel? _searched;

    public PublicShowcaseUseCasesTests()
    {
        var @event = EventTestData.NewModel() with { Status = "PUBLISHED" };

        _eventRepository.SearchPublishedAsync(Arg.Do<EventSearchModel>(filter => _searched = filter), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new EventSearchResultModel([@event], 1));
        _eventRepository.GetPublishedBySlugAsync(EventTestData.SLUG, Arg.Any<CancellationToken>()).Returns(@event);
        _batchRepository.ListByEventIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([
                TicketBatchTestData.NewModel(),
                TicketBatchTestData.NewModel(TicketBatchTestData.Details(name: "VIP", priceCents: 30000, status: "INACTIVE")) with { Id = Guid.NewGuid() },
            ]);
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TicketBatchSalesModel> { [TicketBatchTestData.BATCH_ID] = new(Sold: 497, Reserved: 1) });
        _producerRepository.GetProfilesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ProducerProfileModel(EventTestData.PRODUCER_ID, EventTestData.PRODUCER_SLUG, "Maré Cultural")]);
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);

        _measurements = new MeasurementCollector(_meterFactory);

        var timeProvider = new FakeTimeProvider(NOW);

        _searchUseCase = new SearchPublicEventsUseCase(_producerRepository, _eventRepository, _batchRepository, _unitOfWork, timeProvider, new SearchPublicEventsDiagnostics(_meterFactory), new CapturingLogger<SearchPublicEventsUseCase>());
        _getUseCase = new GetPublicEventUseCase(_producerRepository, _eventRepository, _batchRepository, _unitOfWork, timeProvider, new OrderConfiguration(), new GetPublicEventDiagnostics(_meterFactory), new CapturingLogger<GetPublicEventUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static SearchPublicEventsUseCaseInput Search(string? producer = null, string? category = null, int? page = null)
        => SearchPublicEventsUseCaseInput.Create(producer, category, null, null, null, null, page, null);

    [Fact]
    public async Task Given_PublishedEvent_When_Searching_Should_ReturnSummaryWithoutStock()
    {
        // Arrange & Act
        var result = await _searchUseCase.ExecuteAsync(Search(category: "FESTIVALS", page: 2), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Output!.Items);
        Assert.Equal(EventTestData.SLUG, item.Slug);
        Assert.Equal("Maré Cultural", item.Producer.Name);
        Assert.Equal(12000, item.PriceFromCents);
        Assert.Equal("ON_SALE", item.SaleState);
        Assert.Equal(1, result.Output.TotalItems);
        Assert.Equal("FESTIVALS", _searched!.Category);
        Assert.Equal(20, _searched.Offset);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_StoreFilter_When_Searching_Should_FilterByProducerId()
    {
        // Arrange & Act
        await _searchUseCase.ExecuteAsync(Search(producer: EventTestData.PRODUCER_SLUG), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(EventTestData.PRODUCER_ID, _searched!.ProducerId);
    }

    [Fact]
    public async Task Given_UnknownStore_When_Searching_Should_ReturnEmptyWithoutQueryingEvents()
    {
        // Arrange & Act
        var result = await _searchUseCase.ExecuteAsync(Search(producer: "loja-inexistente"), EventTestData.AuditableInfo());

        // Assert
        Assert.Empty(result.Output!.Items);
        Assert.Equal(0, result.Output.TotalItems);
        await _eventRepository.DidNotReceive().SearchPublishedAsync(Arg.Any<EventSearchModel>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidFilter_When_Searching_Should_NotOpenTransaction()
    {
        // Arrange & Act
        var result = await _searchUseCase.ExecuteAsync(Search(category: "RODEIO"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_FILTER_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_Cancellation_When_Searching_Should_ReturnCanceledNotification()
    {
        // Arrange
        _eventRepository.SearchPublishedAsync(Arg.Any<EventSearchModel>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _searchUseCase.ExecuteAsync(Search(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(SearchPublicEventsUseCase.SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Searching_Should_Rethrow()
    {
        // Arrange
        _batchRepository.ListByEventIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _searchUseCase.ExecuteAsync(Search(), EventTestData.AuditableInfo()));
    }

    [Fact]
    public async Task Given_PublishedEvent_When_Getting_Should_HidePausedBatchAndCapSelectable()
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetPublicEventUseCaseInput.Create(EventTestData.SLUG), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        var batch = Assert.Single(result.Output!.Batches);
        Assert.Equal("1º lote - Pista", batch.Name);
        Assert.Equal(2, batch.MaxSelectable);
        Assert.Equal(6, result.Output.MaxTicketsPerOrder);
        Assert.Equal("ON_SALE", result.Output.SaleState);
        Assert.Equal(EventTestData.PRODUCER_SLUG, result.Output.Producer.Slug);
    }

    [Theory]
    [InlineData("teatro-z")]
    [InlineData("")]
    public async Task Given_UnpublishedOrUnknownSlug_When_Getting_Should_ReportNotFound(string slug)
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetPublicEventUseCaseInput.Create(slug), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetPublicEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_SlugTooLong_When_Getting_Should_ReportNotFoundWithoutQuerying()
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetPublicEventUseCaseInput.Create(new string('a', 200)), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetPublicEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_Cancellation_When_Getting_Should_ReturnCanceledNotification()
    {
        // Arrange
        _eventRepository.GetPublishedBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _getUseCase.ExecuteAsync(GetPublicEventUseCaseInput.Create(EventTestData.SLUG), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetPublicEventUseCase.GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Getting_Should_Rethrow()
    {
        // Arrange
        _producerRepository.GetProfilesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _getUseCase.ExecuteAsync(GetPublicEventUseCaseInput.Create(EventTestData.SLUG), EventTestData.AuditableInfo()));
    }
}
