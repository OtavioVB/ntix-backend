using Application.UseCases.GetCustomerOrder;
using Application.UseCases.GetCustomerOrder.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

public sealed class GetCustomerOrderUseCaseTests : IDisposable
{
    private const string CODE = "NX7K2QH9MP";

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CapturingLogger<GetCustomerOrderUseCase> _logger = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(OrderTestData.NOW));
    private readonly GetCustomerOrderUseCase _useCase;

    public GetCustomerOrderUseCaseTests()
    {
        _orderRepository.GetByCodeAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(OrderTestData.NewModel());
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(OrderTestData.PublishedEventModel());

        _measurements = new MeasurementCollector(_meterFactory);
        _useCase = new GetCustomerOrderUseCase(_orderRepository, _eventRepository, _unitOfWork, _timeProvider, new GetCustomerOrderDiagnostics(_meterFactory), _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static GetCustomerOrderUseCaseInput Input(string? code = CODE, string? customerId = null)
        => GetCustomerOrderUseCaseInput.Create(code, customerId ?? OrderTestData.CUSTOMER_ID.ToString());

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.orders.get.outcomes" && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_OwnOrder_When_Getting_Should_ReturnItWithoutCommitting()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(code: "nx7k2qh9mp"), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CODE, result.Output!.Code);
        Assert.Equal("PENDING_PAYMENT", result.Output.Status);
        Assert.Equal(24000, result.Output.TotalCents);
        Assert.Equal(EventTestData.SLUG, result.Output.Event.Slug);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(GetCustomerOrderDiagnostics.OUTCOME_SERVED);
        Assert.Contains(_logger.Logs, log => log.EventId == 6111 && log.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Given_ReservationPastItsEnd_When_Getting_Should_ShowExpired()
    {
        // Arrange
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal("EXPIRED", result.Output!.Status);
    }

    [Fact]
    public async Task Given_OrderOfAnotherCustomer_When_Getting_Should_ReportOrderNotFound()
    {
        // Arrange: o repositório filtra por cliente e não acha o pedido do outro.
        var other = Guid.NewGuid();

        _orderRepository.GetByCodeAsync(CODE, other, Arg.Any<CancellationToken>()).Returns((OrderModel?)null);

        // Act
        var result = await _useCase.ExecuteAsync(Input(customerId: other.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _eventRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(GetCustomerOrderDiagnostics.OUTCOME_ORDER_NOT_FOUND);
        Assert.Contains(_logger.Logs, log => log.EventId == 6112 && log.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Given_OrderWithoutItsEvent_When_Getting_Should_ReportOrderNotFound()
    {
        // Arrange
        _eventRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((EventModel?)null);

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Theory]
    [InlineData("NX123")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Given_CodeOutOfFormat_When_Getting_Should_ReportOrderNotFoundWithoutTouchingTheDatabase(string? code)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(code: code), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_TokenWithoutCustomerId_When_Getting_Should_ReportOrderNotFound()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(customerId: "nao-e-guid"), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Getting_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded(GetCustomerOrderDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Getting_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        ThenOutcomeRecorded(GetCustomerOrderDiagnostics.OUTCOME_ERROR);
        Assert.Contains(_logger.Logs, log => log.EventId == 6113 && log.Level == LogLevel.Error);
        Assert.Contains(_logger.Logs, log => log.EventId == 6114);
    }

    [Fact]
    public async Task Given_Cancellation_When_Getting_Should_ReturnCanceledNotification()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetCustomerOrderUseCase.GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
