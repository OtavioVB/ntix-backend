using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetOrderPaymentMethods;
using Application.UseCases.GetOrderPaymentMethods.Inputs;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

public sealed class GetOrderPaymentMethodsUseCaseTests : IDisposable
{
    private const string CODE = "NX7K2QH9MP";

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> _rankUseCase = Substitute.For<IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CapturingLogger<GetOrderPaymentMethodsUseCase> _logger = new();
    private readonly GetOrderPaymentMethodsUseCase _useCase;

    public GetOrderPaymentMethodsUseCaseTests()
    {
        _orderRepository.GetByCodeAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(OrderTestData.NewModel());
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<RankPaymentIntegrationsUseCaseOutput>.Success(new RankPaymentIntegrationsUseCaseOutput("PIX", [new RankedIntegrationOutput(1, OrderTestData.INTEGRATION_A_ID, "ABACATEPAY")])));

        _measurements = new MeasurementCollector(_meterFactory);
        _useCase = new GetOrderPaymentMethodsUseCase(_orderRepository, _rankUseCase, _unitOfWork, new GetOrderPaymentMethodsDiagnostics(_meterFactory), _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static GetOrderPaymentMethodsUseCaseInput Input(string? code = CODE, string? customerId = null)
        => GetOrderPaymentMethodsUseCaseInput.Create(code, customerId ?? OrderTestData.CUSTOMER_ID.ToString());

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.methods.outcomes" && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_ProducerWithPixRoute_When_Listing_Should_OfferOnlyPix()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PIX", Assert.Single(result.Output!.Methods).Method);
        await _rankUseCase.Received(PaymentMethodsWithAdapter.ALL.Count).ExecuteAsync(
            Arg.Is<RankPaymentIntegrationsUseCaseInput>(input => input.ProducerId.Value == EventTestData.PRODUCER_ID), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(GetOrderPaymentMethodsDiagnostics.OUTCOME_SERVED);
        Assert.Contains(_logger.Logs, log => log.EventId == 7001 && log.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Given_EmptyRanking_When_Listing_Should_ReturnAnEmptyList()
    {
        // Arrange
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<RankPaymentIntegrationsUseCaseOutput>.Error([Notification.Error("PAYMENT_METHOD_UNAVAILABLE", "indisponível")]));

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Output!.Methods);
    }

    [Fact]
    public async Task Given_OrderOfAnotherCustomer_When_Listing_Should_ReportOrderNotFound()
    {
        // Arrange
        var other = Guid.NewGuid();

        _orderRepository.GetByCodeAsync(CODE, other, Arg.Any<CancellationToken>()).Returns((OrderModel?)null);

        // Act
        var result = await _useCase.ExecuteAsync(Input(customerId: other.ToString()), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetOrderPaymentMethodsUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        ThenOutcomeRecorded(GetOrderPaymentMethodsDiagnostics.OUTCOME_ORDER_NOT_FOUND);
        Assert.Contains(_logger.Logs, log => log.EventId == 7002 && log.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("NX123", null)]
    [InlineData(null, null)]
    [InlineData(CODE, "nao-e-guid")]
    public async Task Given_CodeOrCustomerOutOfFormat_When_Listing_Should_ReportOrderNotFoundWithoutTouchingTheDatabase(string? code, string? customerId)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(code, customerId), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetOrderPaymentMethodsUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Listing_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(Input(), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded(GetOrderPaymentMethodsDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Listing_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo()));
        ThenOutcomeRecorded(GetOrderPaymentMethodsDiagnostics.OUTCOME_ERROR);
        Assert.Contains(_logger.Logs, log => log.EventId == 7003 && log.Level == LogLevel.Error);
        Assert.Contains(_logger.Logs, log => log.EventId == 7004);
    }

    [Fact]
    public async Task Given_Cancellation_When_Listing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(Input(), EventTestData.AuditableInfo());

        // Assert
        Assert.Equal(GetOrderPaymentMethodsUseCase.GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
