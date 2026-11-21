using Application.UseCases.GetProducerEvent;
using Application.UseCases.GetProducerEvent.Inputs;
using Application.UseCases.GetProducerEvents;
using Application.UseCases.GetProducerEvents.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class GetProducerEventsUseCasesTests : IDisposable
{
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ISalesRepository _salesRepository = Substitute.For<ISalesRepository>();
    private readonly FakeTimeProvider _timeProvider = new(EventTestData.NOW);
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly GetProducerEventsUseCase _listUseCase;
    private readonly GetProducerEventUseCase _getUseCase;

    public GetProducerEventsUseCasesTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);

        _measurements = new MeasurementCollector(_meterFactory);

        _listUseCase = new GetProducerEventsUseCase(_producerRepository, _eventRepository, _salesRepository, _timeProvider, _unitOfWork, new GetProducerEventsDiagnostics(_meterFactory), new CapturingLogger<GetProducerEventsUseCase>());
        _getUseCase = new GetProducerEventUseCase(_producerRepository, _eventRepository, _salesRepository, _timeProvider, _unitOfWork, new GetProducerEventDiagnostics(_meterFactory), new CapturingLogger<GetProducerEventUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("DRAFT", "DRAFT")]
    public async Task Given_StatusFilter_When_Listing_Should_PassItToRepositoryAndOnlyRead(string? status, string? expectedFilter)
    {
        // Arrange
        _eventRepository.ListByProducerAsync(EventTestData.PRODUCER_ID, expectedFilter, EventTestData.NOW, Arg.Any<CancellationToken>()).Returns([EventTestData.NewModel()]);

        // Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, status), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EventTestData.SLUG, Assert.Single(result.Output!.Events).Slug);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EventsWithSales_When_Listing_Should_ReadAllSummariesAtOnceAndConvertToCents()
    {
        // Arrange
        _eventRepository.ListByProducerAsync(EventTestData.PRODUCER_ID, null, EventTestData.NOW, Arg.Any<CancellationToken>()).Returns([EventTestData.NewModel()]);
        _salesRepository.GetEventSummariesAsync(EventTestData.PRODUCER_ID, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, EventSalesSummaryModel> { [EventTestData.EVENT_ID] = new(Capacity: 1200, SoldQuantity: 431, GrossAmount: 51720.00m, PaidOrders: 298) });

        // Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, null), EventTestData.AuditableInfo());

        // Assert (RN10)
        var summary = Assert.Single(result.Output!.Events).SalesSummary;
        Assert.Equal(1200, summary.Capacity);
        Assert.Equal(431, summary.SoldQuantity);
        Assert.Equal(5172000, summary.GrossCents);
        Assert.Equal(298, summary.PaidOrders);
        await _salesRepository.Received(1).GetEventSummariesAsync(EventTestData.PRODUCER_ID, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == EventTestData.EVENT_ID), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EventWithoutSales_When_Listing_Should_ReturnAZeroedSummary()
    {
        // Arrange
        _eventRepository.ListByProducerAsync(EventTestData.PRODUCER_ID, null, EventTestData.NOW, Arg.Any<CancellationToken>()).Returns([EventTestData.NewModel()]);
        _salesRepository.GetEventSummariesAsync(EventTestData.PRODUCER_ID, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, EventSalesSummaryModel>());

        // Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, null), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(new Application.UseCases.Base.Events.Outputs.ProducerEventSalesSummaryOutput(0, 0, 0, 0), Assert.Single(result.Output!.Events).SalesSummary);
    }

    [Fact]
    public async Task Given_EventThatHasEnded_When_Getting_Should_ExposeEndedStatus()
    {
        // Arrange
        _timeProvider.Advance(EventTestData.ENDS_AT.AddMinutes(1) - EventTestData.NOW);
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());

        // Act
        var result = await _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create(EventTestData.PRODUCER_SLUG, EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert (RN1)
        Assert.Equal("ENDED", result.Output!.Status);
    }

    [Fact]
    public async Task Given_EndedFilter_When_Listing_Should_PassItWithTheClockToTheRepository()
    {
        // Arrange
        _eventRepository.ListByProducerAsync(EventTestData.PRODUCER_ID, "ENDED", EventTestData.NOW, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, "ENDED"), EventTestData.AuditableInfo());

        // Assert (RN3): sem eventos, nem consulta os resumos.
        Assert.True(result.IsSuccess);
        await _eventRepository.Received(1).ListByProducerAsync(EventTestData.PRODUCER_ID, "ENDED", EventTestData.NOW, Arg.Any<CancellationToken>());
        await _salesRepository.DidNotReceive().GetEventSummariesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnknownStatus_When_Listing_Should_ReportStatusInvalid()
    {
        // Arrange & Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, "CANCELED"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_STATUS_INVALID", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_ProducerNotFound_When_Listing_Should_ReportProducerNotFound()
    {
        // Arrange & Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create("outra-produtora", null), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetProducerEventsUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_Cancellation_When_Listing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _listUseCase.ExecuteAsync(GetProducerEventsUseCaseInput.Create(EventTestData.PRODUCER_SLUG, null), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetProducerEventsUseCase.GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_ExistingEvent_When_Getting_Should_ReturnItInUtc()
    {
        // Arrange
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());

        // Act
        var result = await _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create(EventTestData.PRODUCER_SLUG, EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EventTestData.EVENT_ID, result.Output!.Id);
        Assert.Equal(DateTimeKind.Utc, result.Output.StartsAt.Kind);
        Assert.Equal("AGE_16", result.Output.AgeRating);
    }

    [Theory]
    [InlineData("nao-e-um-guid")]
    [InlineData("7d3a5b1c-0000-4000-8000-000000000000")]
    public async Task Given_MissingEvent_When_Getting_Should_ReportNotFound(string eventId)
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create(EventTestData.PRODUCER_SLUG, eventId), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetProducerEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_ProducerNotFound_When_Getting_Should_ReportProducerNotFound()
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create("outra-produtora", EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetProducerEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidSlug_When_Getting_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create("../x", EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("SLUG_INVALID", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Getting_Should_Rethrow()
    {
        // Arrange
        _eventRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _getUseCase.ExecuteAsync(GetProducerEventUseCaseInput.Create(EventTestData.PRODUCER_SLUG, EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo()));
    }
}
