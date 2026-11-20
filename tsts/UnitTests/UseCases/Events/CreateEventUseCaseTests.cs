using Application.UseCases.CreateEvent;
using Application.UseCases.CreateEvent.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class CreateEventUseCaseTests : IDisposable
{
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CapturingLogger<CreateEventUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CreateEventUseCase _useCase;

    private readonly List<EventModel> _attempts = [];

    public CreateEventUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);
        _eventRepository
            .CreateAsync(Arg.Do<EventModel>(_attempts.Add), Arg.Any<CancellationToken>())
            .Returns(EventCreationResult.Created);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new CreateEventUseCase(
            producerRepository: _producerRepository,
            eventRepository: _eventRepository,
            unitOfWork: _unitOfWork,
            timeProvider: new FakeTimeProvider(EventTestData.NOW),
            diagnostics: new CreateEventDiagnostics(_meterFactory),
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static CreateEventUseCaseInput BuildInput(string title = EventTestData.TITLE, string? category = "FESTIVALS", DateTime? startsAt = null)
        => CreateEventUseCaseInput.Create(
            producerSlug: EventTestData.PRODUCER_SLUG,
            title: title,
            description: "Três palcos.",
            category: category,
            startsAt: EventTestData.Iso(startsAt ?? EventTestData.STARTS_AT),
            endsAt: EventTestData.Iso((startsAt ?? EventTestData.STARTS_AT).AddHours(9)),
            doorsOpenAt: null,
            venueName: "Parque das Dunas",
            address: "Av. Beira-Mar, 1200",
            city: "Recife",
            state: "PE",
            ageRating: "AGE_16",
            imageUrl: null);

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.events.create.outcomes" && measurement.Value == 1).Outcome);

    private async Task ThenRolledBackWithoutSaving()
    {
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.Empty(_attempts);
    }

    [Fact]
    public async Task Given_ValidEvent_When_Executing_Should_SaveDraftWithSlugAndCommit()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        var saved = Assert.Single(_attempts);
        Assert.Equal(EventTestData.PRODUCER_ID, saved.ProducerId);
        Assert.Equal(EventTestData.SLUG, saved.Slug);
        Assert.Equal("DRAFT", saved.Status);
        Assert.Equal(EventTestData.CORRELATION_ID, saved.CreatedCorrelationId);
        Assert.Equal(EventTestData.NOW, saved.CreatedAt);

        Assert.Equal(saved.Id, result.Output!.Id);
        Assert.Equal("DRAFT", result.Output.Status);
        Assert.Null(result.Output.DoorsOpenAt);
        ThenOutcomeRecorded(CreateEventDiagnostics.OUTCOME_CREATED);
    }

    [Fact]
    public async Task Given_SlugAlreadyUsed_When_Executing_Should_UseRandomVersion()
    {
        // Arrange
        _eventRepository.ExistsBySlugAsync(EventTestData.SLUG, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Matches("^festival-horizonte-[a-z0-9]{5}$", Assert.Single(_attempts).Slug);
    }

    [Fact]
    public async Task Given_SlugTakenConcurrently_When_Executing_Should_RetryWithRandomVersion()
    {
        // Arrange
        _eventRepository.CreateAsync(Arg.Any<EventModel>(), Arg.Any<CancellationToken>())
            .Returns(EventCreationResult.SlugAlreadyExists, EventCreationResult.Created);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, _attempts.Count);
        Assert.Equal(EventTestData.SLUG, _attempts[0].Slug);
        Assert.Matches("^festival-horizonte-[a-z0-9]{5}$", _attempts[1].Slug);
        Assert.Equal(_attempts[1].Slug, result.Output!.Slug);
    }

    [Fact]
    public async Task Given_InvalidData_When_Executing_Should_NotOpenTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(category: "RODEIO"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_CATEGORY_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateEventDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_StartInThePast_When_Executing_Should_RollBack()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(startsAt: EventTestData.NOW.AddHours(-1)), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_PERIOD_INVALID", Assert.Single(result.Notifications).Code);
        await ThenRolledBackWithoutSaving();
    }

    [Fact]
    public async Task Given_ProducerNotFound_When_Executing_Should_RollBack()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await ThenRolledBackWithoutSaving();
        ThenOutcomeRecorded(CreateEventDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_ReportIt()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Executing_Should_RollBackAndRethrow()
    {
        // Arrange
        _eventRepository.CreateAsync(Arg.Any<EventModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo()));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateEventDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_Executing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateEventUseCase.CREATE_EVENT_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }
}
