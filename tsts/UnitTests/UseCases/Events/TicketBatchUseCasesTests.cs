using Application.UseCases.CreateTicketBatch;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetTicketBatches;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.UpdateTicketBatch;
using Application.UseCases.UpdateTicketBatch.Inputs;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class TicketBatchUseCasesTests : IDisposable
{
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ITicketBatchRepository _batchRepository = Substitute.For<ITicketBatchRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly FakeTimeProvider _timeProvider = new(EventTestData.NOW);
    private readonly CreateTicketBatchUseCase _createUseCase;
    private readonly UpdateTicketBatchUseCase _updateUseCase;
    private readonly GetTicketBatchesUseCase _listUseCase;

    private TicketBatchModel? _saved;

    public TicketBatchUseCasesTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);
        _eventRepository.GetByIdForUpdateAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());
        _batchRepository.CreateAsync(Arg.Do<TicketBatchModel>(model => _saved = model), Arg.Any<CancellationToken>()).Returns(TicketBatchWriteResult.Written);
        _batchRepository.UpdateAsync(Arg.Do<TicketBatchModel>(model => _saved = model), Arg.Any<CancellationToken>()).Returns(TicketBatchWriteResult.Written);
        _batchRepository.GetByIdForUpdateAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, TicketBatchTestData.BATCH_ID, Arg.Any<CancellationToken>()).Returns(TicketBatchTestData.NewModel());
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().ToDictionary(id => id, _ => new TicketBatchSalesModel(0, 0)));

        _measurements = new MeasurementCollector(_meterFactory);

        _createUseCase = new CreateTicketBatchUseCase(_producerRepository, _eventRepository, _batchRepository, _unitOfWork, _timeProvider, new CreateTicketBatchDiagnostics(_meterFactory), new CapturingLogger<CreateTicketBatchUseCase>());
        _updateUseCase = new UpdateTicketBatchUseCase(_producerRepository, _eventRepository, _batchRepository, _unitOfWork, _timeProvider, new UpdateTicketBatchDiagnostics(_meterFactory), new CapturingLogger<UpdateTicketBatchUseCase>());
        _listUseCase = new GetTicketBatchesUseCase(_producerRepository, _eventRepository, _batchRepository, _unitOfWork, _timeProvider, new GetTicketBatchesDiagnostics(_meterFactory), new CapturingLogger<GetTicketBatchesUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void ThenOutcomeRecorded(string instrument, string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == instrument && measurement.Value == 1).Outcome);

    private static CreateTicketBatchUseCaseInput CreateInput(string? eventId = null, long priceCents = 12000)
        => CreateTicketBatchUseCaseInput.Create(EventTestData.PRODUCER_SLUG, eventId ?? EventTestData.EVENT_ID.ToString(), TicketBatchTestData.Details(priceCents: priceCents));

    private static UpdateTicketBatchUseCaseInput UpdateInput(string? batchId = null, int quantity = 300, string status = "INACTIVE")
        => UpdateTicketBatchUseCaseInput.Create(EventTestData.PRODUCER_SLUG, EventTestData.EVENT_ID.ToString(), batchId ?? TicketBatchTestData.BATCH_ID.ToString(), TicketBatchTestData.Details(quantity: quantity, status: status));

    [Fact]
    public async Task Given_ValidBatch_When_Creating_Should_SavePriceInReaisAndReturnCents()
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.Equal(120.00m, _saved!.Price);
        Assert.Equal(EventTestData.EVENT_ID, _saved.EventId);
        Assert.Equal(EventTestData.PRODUCER_ID, _saved.ProducerId);
        Assert.Equal(12000, result.Output!.PriceCents);
        Assert.Equal(500, result.Output.AvailableQuantity);
        Assert.Equal("UPCOMING", result.Output.SaleState);
        ThenOutcomeRecorded("ntix.events.batches.create.outcomes", CreateTicketBatchDiagnostics.OUTCOME_CREATED);
    }

    [Fact]
    public async Task Given_NameAlreadyUsedInEvent_When_Creating_Should_ReportConflict()
    {
        // Arrange
        _batchRepository.CreateAsync(Arg.Any<TicketBatchModel>(), Arg.Any<CancellationToken>()).Returns(TicketBatchWriteResult.NameAlreadyExists);

        // Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateTicketBatchUseCase.BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("ntix.events.batches.create.outcomes", CreateTicketBatchDiagnostics.OUTCOME_NAME_ALREADY_EXISTS);
    }

    [Theory]
    [InlineData("nao-e-um-guid")]
    [InlineData("7d3a5b1c-0000-4000-8000-000000000000")]
    public async Task Given_EventMissing_When_Creating_Should_ReportEventNotFound(string eventId)
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(eventId), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateTicketBatchUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidPrice_When_Creating_Should_NotOpenTransaction()
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(priceCents: 50), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("BATCH_PRICE_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EventAlreadyStarted_When_Creating_Should_RollBack()
    {
        // Arrange
        _timeProvider.Advance(EventTestData.STARTS_AT - EventTestData.NOW + TimeSpan.FromMinutes(1));

        // Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_ALREADY_STARTED", Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("ntix.events.batches.create.outcomes", CreateTicketBatchDiagnostics.OUTCOME_EVENT_ALREADY_STARTED);
    }

    [Fact]
    public async Task Given_Cancellation_When_Creating_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _createUseCase.ExecuteAsync(CreateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateTicketBatchUseCase.CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_Pause_When_Updating_Should_SaveInactiveAndCommit()
    {
        // Arrange & Act
        var result = await _updateUseCase.ExecuteAsync(UpdateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", _saved!.Status);
        Assert.Equal(300, _saved.Quantity);
        Assert.Equal("PAUSED", result.Output!.SaleState);
        ThenOutcomeRecorded("ntix.events.batches.update.outcomes", UpdateTicketBatchDiagnostics.OUTCOME_UPDATED);
    }

    [Fact]
    public async Task Given_QuantityBelowSoldAndReserved_When_Updating_Should_ReportQuantityBelowSold()
    {
        // Arrange
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TicketBatchSalesModel> { [TicketBatchTestData.BATCH_ID] = new(Sold: 250, Reserved: 60) });

        // Act
        var result = await _updateUseCase.ExecuteAsync(UpdateInput(quantity: 300), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("BATCH_QUANTITY_BELOW_SOLD", Assert.Single(result.Notifications).Code);
        await _batchRepository.DidNotReceive().UpdateAsync(Arg.Any<TicketBatchModel>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("ntix.events.batches.update.outcomes", UpdateTicketBatchDiagnostics.OUTCOME_QUANTITY_BELOW_SOLD);
    }

    [Fact]
    public async Task Given_BatchMissing_When_Updating_Should_ReportBatchNotFound()
    {
        // Arrange & Act
        var result = await _updateUseCase.ExecuteAsync(UpdateInput(batchId: Guid.NewGuid().ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(UpdateTicketBatchUseCase.BATCH_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        ThenOutcomeRecorded("ntix.events.batches.update.outcomes", UpdateTicketBatchDiagnostics.OUTCOME_BATCH_NOT_FOUND);
    }

    [Fact]
    public async Task Given_NameConflict_When_Updating_Should_ReportConflict()
    {
        // Arrange
        _batchRepository.UpdateAsync(Arg.Any<TicketBatchModel>(), Arg.Any<CancellationToken>()).Returns(TicketBatchWriteResult.NameAlreadyExists);

        // Act
        var result = await _updateUseCase.ExecuteAsync(UpdateInput(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(UpdateTicketBatchUseCase.BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Updating_Should_Rethrow()
    {
        // Arrange
        _batchRepository.UpdateAsync(Arg.Any<TicketBatchModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _updateUseCase.ExecuteAsync(UpdateInput(), EventTestData.AuditableInfo()));
        ThenOutcomeRecorded("ntix.events.batches.update.outcomes", UpdateTicketBatchDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_EventWithBatches_When_Listing_Should_ReturnAvailabilityAndOnlyRead()
    {
        // Arrange
        _batchRepository.ListByEventAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns([TicketBatchTestData.NewModel()]);
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TicketBatchSalesModel> { [TicketBatchTestData.BATCH_ID] = new(Sold: 120, Reserved: 30) });

        // Act
        var result = await _listUseCase.ExecuteAsync(GetTicketBatchesUseCaseInput.Create(EventTestData.PRODUCER_SLUG, EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        var batch = Assert.Single(result.Output!.Batches);
        Assert.Equal(120, batch.SoldQuantity);
        Assert.Equal(30, batch.ReservedQuantity);
        Assert.Equal(350, batch.AvailableQuantity);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EventMissing_When_Listing_Should_ReportEventNotFound()
    {
        // Arrange & Act
        var result = await _listUseCase.ExecuteAsync(GetTicketBatchesUseCaseInput.Create("outra-produtora", EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetTicketBatchesUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
