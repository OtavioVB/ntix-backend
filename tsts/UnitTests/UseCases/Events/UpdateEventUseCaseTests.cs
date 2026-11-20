using Application.UseCases.UpdateEvent;
using Application.UseCases.UpdateEvent.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class UpdateEventUseCaseTests : IDisposable
{
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CapturingLogger<UpdateEventUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly FakeTimeProvider _timeProvider = new(EventTestData.NOW);
    private readonly UpdateEventUseCase _useCase;

    private EventModel? _saved;

    public UpdateEventUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);
        _eventRepository.GetByIdForUpdateAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());
        _eventRepository.UpdateAsync(Arg.Do<EventModel>(model => _saved = model), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new UpdateEventUseCase(
            producerRepository: _producerRepository,
            eventRepository: _eventRepository,
            unitOfWork: _unitOfWork,
            timeProvider: _timeProvider,
            diagnostics: new UpdateEventDiagnostics(_meterFactory),
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static UpdateEventUseCaseInput BuildInput(string? eventId = null, string title = "Festival Horizonte 2027", string state = "PE")
        => UpdateEventUseCaseInput.Create(
            producerSlug: EventTestData.PRODUCER_SLUG,
            eventId: eventId ?? EventTestData.EVENT_ID.ToString(),
            title: title,
            description: null,
            category: "FESTIVALS",
            startsAt: EventTestData.Iso(EventTestData.STARTS_AT),
            endsAt: EventTestData.Iso(EventTestData.ENDS_AT),
            doorsOpenAt: null,
            venueName: "Parque das Dunas",
            address: "Av. Beira-Mar, 1200",
            city: "Recife",
            state: state,
            ageRating: "AGE_16",
            imageUrl: null);

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.events.update.outcomes" && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_EventNotStarted_When_Executing_Should_ReplaceFieldsKeepSlugAndCommit()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        Assert.NotNull(_saved);
        Assert.Equal("Festival Horizonte 2027", _saved.Title);
        Assert.Equal(EventTestData.SLUG, _saved.Slug);
        Assert.Null(_saved.Description);
        Assert.Null(_saved.ImageUrl);
        Assert.Equal(EventTestData.NOW, _saved.UpdatedAt);
        Assert.Equal(EventTestData.SLUG, result.Output!.Slug);
        ThenOutcomeRecorded(UpdateEventDiagnostics.OUTCOME_UPDATED);
    }

    [Fact]
    public async Task Given_ChangedFields_When_Executing_Should_LogOnlyTheirNames()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(title: "Título secreto"), EventTestData.AuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs, entry => entry.Message.Contains("alterado"));
        Assert.Contains("title", log.Message);
        Assert.DoesNotContain("Título secreto", log.Message);
    }

    [Fact]
    public async Task Given_EventAlreadyStarted_When_Executing_Should_RollBackWithAlreadyStarted()
    {
        // Arrange
        _timeProvider.Advance(EventTestData.STARTS_AT - EventTestData.NOW + TimeSpan.FromMinutes(1));

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_ALREADY_STARTED", Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _eventRepository.DidNotReceive().UpdateAsync(Arg.Any<EventModel>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(UpdateEventDiagnostics.OUTCOME_ALREADY_STARTED);
    }

    [Theory]
    [InlineData("nao-e-um-guid")]
    [InlineData("7d3a5b1c-0000-4000-8000-000000000000")]
    public async Task Given_EventOfAnotherProducerOrInvalidId_When_Executing_Should_ReportNotFound(string eventId)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(eventId: eventId), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(UpdateEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(UpdateEventDiagnostics.OUTCOME_EVENT_NOT_FOUND);
    }

    [Fact]
    public async Task Given_ProducerNotFound_When_Executing_Should_ReportProducerNotFound()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(UpdateEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidData_When_Executing_Should_NotOpenTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(state: "XX"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_STATE_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(UpdateEventDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Executing_Should_RollBackAndRethrow()
    {
        // Arrange
        _eventRepository.UpdateAsync(Arg.Any<EventModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo()));
        ThenOutcomeRecorded(UpdateEventDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_Executing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(UpdateEventUseCase.UPDATE_EVENT_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
