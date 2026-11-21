using Application.UseCases.PublishEvent;
using Application.UseCases.PublishEvent.Inputs;
using Domain.BoundedContexts.EventContext.Events;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Events;

public sealed class PublishEventUseCaseTests : IDisposable
{
    private static readonly Guid INTEGRATION_ID = Guid.Parse("6b0f1a2b-3c4d-4e5f-8a9b-0c1d2e3f4a5b");

    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ISalesRepository _salesRepository = Substitute.For<ISalesRepository>();
    private readonly ITicketBatchRepository _batchRepository = Substitute.For<ITicketBatchRepository>();
    private readonly IPaymentProviderRepository _providerRepository = Substitute.For<IPaymentProviderRepository>();
    private readonly IProducerIntegrationRepository _integrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly IPaymentRouteRepository _routeRepository = Substitute.For<IPaymentRouteRepository>();
    private readonly IOutboxRepository _outboxRepository = Substitute.For<IOutboxRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly PublishEventUseCase _useCase;

    private EventModel? _saved;
    private OutboxEventModel? _outboxEvent;

    public PublishEventUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(EventTestData.PRODUCER_SLUG, Arg.Any<CancellationToken>()).Returns(EventTestData.PRODUCER_ID);
        _eventRepository.GetByIdForUpdateAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel());
        _eventRepository.UpdateStatusAsync(Arg.Do<EventModel>(model => _saved = model), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _outboxRepository.AddAsync(Arg.Do<OutboxEventModel>(model => _outboxEvent = model), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _batchRepository.ListByEventAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns([TicketBatchTestData.NewModel()]);
        _providerRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel()]);
        _integrationRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>()).Returns([PaymentTestData.IntegrationModel(INTEGRATION_ID)]);
        GivenRoute("PIX");

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new PublishEventUseCase(
            producerRepository: _producerRepository,
            eventRepository: _eventRepository,
            salesRepository: _salesRepository,
            ticketBatchRepository: _batchRepository,
            paymentProviderRepository: _providerRepository,
            producerIntegrationRepository: _integrationRepository,
            paymentRouteRepository: _routeRepository,
            outboxRepository: _outboxRepository,
            unitOfWork: _unitOfWork,
            timeProvider: new FakeTimeProvider(EventTestData.NOW),
            diagnostics: new PublishEventDiagnostics(_meterFactory),
            logger: new CapturingLogger<PublishEventUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void GivenRoute(params string[] methods)
        => _routeRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns(methods.Select(method => new PaymentRouteModel(Guid.NewGuid(), EventTestData.PRODUCER_ID, method, 1, INTEGRATION_ID, EventTestData.NOW, Guid.NewGuid())).ToArray());

    private static PublishEventUseCaseInput Input(string? eventId = null)
        => PublishEventUseCaseInput.Create(EventTestData.PRODUCER_SLUG, eventId ?? EventTestData.EVENT_ID.ToString());

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.events.publish.outcomes" && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_ReadyEvent_When_Publishing_Should_SavePublishedAndWriteOutboxInSameTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PUBLISHED", result.Output!.Status);
        Assert.Equal("PUBLISHED", _saved!.Status);
        Assert.Equal(EventPublishedEvent.EVENT_TYPE, _outboxEvent!.EventType);
        Assert.Equal(EventTestData.EVENT_ID, _outboxEvent.AggregateId);
        Assert.Contains(EventTestData.SLUG, _outboxEvent.Payload);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_PUBLISHED);
    }

    [Fact]
    public async Task Given_RouteOnlyForMethodWithoutAdapter_When_Publishing_Should_ReportWithoutPaymentMethod()
    {
        // Arrange
        GivenRoute("BOLETO", "CREDIT_CARD");

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_WITHOUT_PAYMENT_METHOD", Assert.Single(result.Notifications).Code);
        await _outboxRepository.DidNotReceive().AddAsync(Arg.Any<OutboxEventModel>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_WITHOUT_PAYMENT_METHOD);
    }

    [Fact]
    public async Task Given_PixIntegrationInactive_When_Publishing_Should_ReportWithoutPaymentMethod()
    {
        // Arrange
        _integrationRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>()).Returns([PaymentTestData.IntegrationModel(INTEGRATION_ID, status: "INACTIVE")]);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_WITHOUT_PAYMENT_METHOD", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_NoRoutes_When_Publishing_Should_ReportWithoutPaymentMethod()
    {
        // Arrange
        GivenRoute();

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_WITHOUT_PAYMENT_METHOD", Assert.Single(result.Notifications).Code);
        await _integrationRepository.DidNotReceive().GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NoBatches_When_Publishing_Should_ReportWithoutBatchAndRollBack()
    {
        // Arrange
        _batchRepository.ListByEventAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_WITHOUT_BATCH", Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _eventRepository.DidNotReceive().UpdateStatusAsync(Arg.Any<EventModel>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_WITHOUT_BATCH);
    }

    [Fact]
    public async Task Given_PublishedEvent_When_Publishing_Should_ReportNotDraft()
    {
        // Arrange
        _eventRepository.GetByIdForUpdateAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(EventTestData.NewModel() with { Status = "PUBLISHED" });

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EVENT_NOT_DRAFT", Assert.Single(result.Notifications).Code);
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_NOT_DRAFT);
    }

    [Fact]
    public async Task Given_EventMissing_When_Publishing_Should_ReportEventNotFound()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(Guid.NewGuid().ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(PublishEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_EVENT_NOT_FOUND);
    }

    [Fact]
    public async Task Given_InvalidSlug_When_Publishing_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(PublishEventUseCaseInput.Create("../x", EventTestData.EVENT_ID.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("SLUG_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Publishing_Should_RollBackAndRethrow()
    {
        // Arrange
        _outboxRepository.AddAsync(Arg.Any<OutboxEventModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(PublishEventDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_Publishing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(PublishEventUseCase.PUBLISH_EVENT_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
