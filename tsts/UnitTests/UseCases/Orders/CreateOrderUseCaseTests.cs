using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateOrder;
using Application.UseCases.CreateOrder.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

public sealed class CreateOrderUseCaseTests : IDisposable
{
    private const string BATCH_LOCK_INSTRUMENT = "ntix.orders.batch_lock.duration";

    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly ITicketBatchRepository _batchRepository = Substitute.For<ITicketBatchRepository>();
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IProducerCustomerRepository _producerCustomerRepository = Substitute.For<IProducerCustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CapturingLogger<CreateOrderUseCase> _logger = new();
    private readonly CreateOrderUseCase _useCase;
    private readonly List<OrderModel> _created = [];

    public CreateOrderUseCaseTests()
    {
        _eventRepository.GetPublishedBySlugAsync(EventTestData.SLUG, Arg.Any<CancellationToken>()).Returns(OrderTestData.PublishedEventModel());
        _batchRepository.ListByIdsForUpdateAsync(EventTestData.EVENT_ID, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([TicketBatchTestData.NewModel()]);
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TicketBatchSalesModel>());
        _orderRepository.CreateAsync(Arg.Do<OrderModel>(order => _created.Add(order)), Arg.Any<CancellationToken>()).Returns(OrderCreationResult.Created);
        _producerCustomerRepository.LinkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        _measurements = new MeasurementCollector(_meterFactory);
        _useCase = new CreateOrderUseCase(
            _eventRepository, _batchRepository, _orderRepository, _producerCustomerRepository, _unitOfWork,
            new FakeTimeProvider(new DateTimeOffset(OrderTestData.NOW)), new OrderConfiguration(), new CreateOrderDiagnostics(_meterFactory), _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static CreateOrderUseCaseInput Input(int quantity = 2, string? eventSlug = EventTestData.SLUG, string? batchId = null)
        => CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), eventSlug, "HUB", [(batchId ?? TicketBatchTestData.BATCH_ID.ToString(), quantity)]);

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.orders.create.outcomes" && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_ValidRequest_When_Creating_Should_ReserveCommitAndReturnTheOrder()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);

        var output = result.Output!;

        Assert.Equal("PENDING_PAYMENT", output.Status);
        Assert.Equal("HUB", output.SalesChannel);
        Assert.Equal(24000, output.TotalCents);
        Assert.Equal(OrderTestData.NOW, output.CreatedAt);
        Assert.Equal(OrderTestData.NOW.AddMinutes(15), output.ExpiresAt);
        Assert.Equal(EventTestData.SLUG, output.Event.Slug);
        Assert.Equal("Festival Horizonte", output.Event.Title);

        var item = Assert.Single(output.Items);

        Assert.Equal(("1º lote - Pista", 2, 12000L, 24000L), (item.BatchName, item.Quantity, item.UnitPriceCents, item.TotalCents));
        Assert.Empty(output.Tickets);

        var saved = Assert.Single(_created);

        Assert.Equal(output.Code, saved.Code);
        Assert.Equal(240.00m, saved.TotalAmount);
        Assert.Equal(EventTestData.PRODUCER_ID, saved.ProducerId);
        Assert.Equal(OrderTestData.CUSTOMER_ID, saved.CustomerId);
        Assert.Equal(120.00m, Assert.Single(saved.Items).UnitPrice);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_CREATED);
        Assert.Contains(_measurements.Durations, duration => duration.Instrument == BATCH_LOCK_INSTRUMENT);
    }

    [Fact]
    public async Task Given_FirstOrderWithTheProducer_When_Creating_Should_LinkInTheSameTransactionAndLogIt()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        await _producerCustomerRepository.Received(1).LinkAsync(EventTestData.PRODUCER_ID, OrderTestData.CUSTOMER_ID, OrderTestData.NOW, EventTestData.CORRELATION_ID, Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Logs, log => log.EventId == 6101 && log.Level == LogLevel.Information);
        Assert.Contains(_logger.Logs, log => log.EventId == 6103);
    }

    [Fact]
    public async Task Given_CustomerAlreadyLinked_When_Creating_Should_NotLogANewLink()
    {
        // Arrange
        _producerCustomerRepository.LinkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(_logger.Logs, log => log.EventId == 6103);
    }

    [Fact]
    public async Task Given_UnknownEvent_When_Creating_Should_ReportEventNotFoundAndRollBack()
    {
        // Arrange
        _eventRepository.GetPublishedBySlugAsync("outro-evento", Arg.Any<CancellationToken>()).Returns((EventModel?)null);

        // Act
        var result = await _useCase.ExecuteAsync(Input(eventSlug: "outro-evento"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateOrderUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _orderRepository.DidNotReceive().CreateAsync(Arg.Any<OrderModel>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_EVENT_NOT_FOUND);
    }

    [Fact]
    public async Task Given_SlugLongerThanAnyEvent_When_Creating_Should_ReportEventNotFoundWithoutTouchingTheDatabase()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(eventSlug: new string('a', 300)), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateOrderUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_EVENT_NOT_FOUND);
    }

    [Fact]
    public async Task Given_InvalidInput_When_Creating_Should_ReportValidationWithoutOpeningATransaction()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), null, null, null);

        // Act
        var result = await _useCase.ExecuteAsync(input, EventTestData.AuditableInfo());

        // Assert
        var codes = result.Notifications.Select(notification => notification.Code).ToArray();

        Assert.Contains(CreateOrderUseCaseInput.ORDER_EVENT_REQUIRED_NOTIFICATION_CODE, codes);
        Assert.Contains(CreateOrderUseCaseInput.ORDER_ITEMS_REQUIRED_NOTIFICATION_CODE, codes);
        Assert.Contains(SalesChannelValueObject.ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_CODE, codes);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Creating_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_MoreThanTheLimit_When_Creating_Should_RefuseAndRollBack()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(quantity: 7), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(Order.ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_BatchOutsideTheEvent_When_Creating_Should_ReportBatchNotFound()
    {
        // Arrange: a consulta filtrada por evento não devolve o lote de outro evento.
        _batchRepository.ListByIdsForUpdateAsync(EventTestData.EVENT_ID, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await _useCase.ExecuteAsync(Input(batchId: Guid.NewGuid().ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(Order.BATCH_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_BatchNotOnSale_When_Creating_Should_ReportNotOnSale()
    {
        // Arrange
        _batchRepository.ListByIdsForUpdateAsync(EventTestData.EVENT_ID, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([TicketBatchTestData.NewModel(TicketBatchTestData.Details(status: "INACTIVE"))]);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_NOT_ON_SALE);
    }

    [Fact]
    public async Task Given_SoldAndReservedLeaveOneTicket_When_AskingForTwo_Should_ReportInsufficientAvailability()
    {
        // Arrange
        _batchRepository.GetSalesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TicketBatchSalesModel> { [TicketBatchTestData.BATCH_ID] = new(Sold: 497, Reserved: 2) });

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _producerCustomerRepository.DidNotReceive().LinkAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_INSUFFICIENT_AVAILABILITY);
        Assert.Contains(_logger.Logs, log => log.EventId == 6102 && log.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Given_CodeAlreadyTaken_When_Creating_Should_DrawAnotherCodeAndKeepTheReservation()
    {
        // Arrange
        _orderRepository.CreateAsync(Arg.Any<OrderModel>(), Arg.Any<CancellationToken>())
            .Returns(OrderCreationResult.CodeAlreadyExists, OrderCreationResult.Created);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, _created.Count);
        Assert.NotEqual(_created[0].Code, _created[1].Code);
        Assert.Equal(_created[1].Code, result.Output!.Code);
        Assert.Equal(_created[0].Id, _created[1].Id);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EveryCodeTaken_When_Creating_Should_GiveUpRollBackAndRethrow()
    {
        // Arrange
        _orderRepository.CreateAsync(Arg.Any<OrderModel>(), Arg.Any<CancellationToken>()).Returns(OrderCreationResult.CodeAlreadyExists);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        await _orderRepository.Received(5).CreateAsync(Arg.Any<OrderModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Creating_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _eventRepository.GetPublishedBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        ThenOutcomeRecorded(CreateOrderDiagnostics.OUTCOME_ERROR);
        Assert.Contains(_logger.Logs, log => log.EventId == 6104 && log.Level == LogLevel.Error);
        Assert.Contains(_logger.Logs, log => log.EventId == 6105);
    }

    [Fact]
    public async Task Given_Cancellation_When_Creating_Should_ReturnCanceledNotification()
    {
        // Arrange
        _eventRepository.GetPublishedBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(CreateOrderUseCase.CREATE_ORDER_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }
}
