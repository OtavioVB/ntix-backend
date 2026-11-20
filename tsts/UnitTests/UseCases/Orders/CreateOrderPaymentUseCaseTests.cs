using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.Http.Payments.Models;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using System.Text;
using TestSupport;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

public sealed class CreateOrderPaymentUseCaseTests : IDisposable
{
    private const string KEY_A = "abc_dev_chave-a";
    private const string KEY_B = "abc_dev_chave-b";
    private const string CODE = "NX7K2QH9MP";

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IPaymentRepository _paymentRepository = Substitute.For<IPaymentRepository>();
    private readonly IProducerIntegrationRepository _integrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput> _rankUseCase = Substitute.For<IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>>();
    private readonly ICredentialCipher _cipher = Substitute.For<ICredentialCipher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakePixChargeClient _pixClient = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(OrderTestData.NOW));
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CapturingLogger<CreateOrderPaymentUseCase> _logger = new();
    private readonly List<PaymentModel> _created = [];
    private readonly List<PaymentModel> _statusUpdates = [];
    private readonly List<OrderModel> _orderUpdates = [];

    public CreateOrderPaymentUseCaseTests()
    {
        _orderRepository.GetByCodeAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(_ => OrderTestData.NewModel());
        _orderRepository.GetByCodeForUpdateAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(_ => OrderTestData.NewModel());
        _orderRepository.UpdateStatusAsync(Arg.Do<OrderModel>(order => _orderUpdates.Add(order)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _paymentRepository.CreateAsync(Arg.Do<PaymentModel>(payment => _created.Add(payment)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _paymentRepository.UpdateStatusAsync(Arg.Do<PaymentModel>(payment => _statusUpdates.Add(payment)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _customerRepository.GetByIdAsync(OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(CustomerSessionTestData.NewCustomer(OrderTestData.CUSTOMER_ID));
        _eventRepository.GetByIdAsync(EventTestData.PRODUCER_ID, EventTestData.EVENT_ID, Arg.Any<CancellationToken>()).Returns(OrderTestData.PublishedEventModel());
        _integrationRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns([Integration(OrderTestData.INTEGRATION_A_ID, KEY_A), Integration(OrderTestData.INTEGRATION_B_ID, KEY_B)]);
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Ranking((1, OrderTestData.INTEGRATION_A_ID), (2, OrderTestData.INTEGRATION_B_ID)));

        // O texto "cifrado" de teste é a própria chave: a decifragem devolve os mesmos bytes.
        _cipher.Decrypt(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(call => call.ArgAt<byte[]>(0));

        _measurements = new MeasurementCollector(_meterFactory);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private CreateOrderPaymentUseCase BuildUseCase(PaymentConfiguration? configuration = null)
        => new(
            _orderRepository, _paymentRepository, _integrationRepository, _customerRepository, _eventRepository, _rankUseCase, [_pixClient], _cipher,
            _unitOfWork, _timeProvider, configuration ?? new PaymentConfiguration(), new CreateOrderPaymentDiagnostics(_meterFactory), _logger);

    private static ProducerIntegrationModel Integration(Guid id, string? apiKey)
        => new(
            Id: id, ProducerId: EventTestData.PRODUCER_ID, ProviderId: Guid.NewGuid(), Name: "Conta", Status: "ACTIVE",
            CreatedAt: OrderTestData.NOW, CreatedCorrelationId: Guid.NewGuid(), UpdatedAt: OrderTestData.NOW, UpdatedCorrelationId: Guid.NewGuid(),
            Credentials: apiKey is null ? [] : [new ProducerIntegrationCredentialModel("API_KEY", Encoding.UTF8.GetBytes(apiKey), "e-a", OrderTestData.NOW, Guid.NewGuid())]);

    private static MethodResult<RankPaymentIntegrationsUseCaseOutput> Ranking(params (int Priority, Guid Id)[] integrations)
        => MethodResult<RankPaymentIntegrationsUseCaseOutput>.Success(new RankPaymentIntegrationsUseCaseOutput(
            "PIX", integrations.Select(item => new RankedIntegrationOutput(item.Priority, item.Id, "ABACATEPAY")).ToArray()));

    private static CreateOrderPaymentUseCaseInput Input(string? code = CODE, string? customerId = null, string? method = "PIX")
        => CreateOrderPaymentUseCaseInput.Create(code, customerId ?? OrderTestData.CUSTOMER_ID.ToString(), method);

    private Task<MethodResult<CreateOrderPaymentOutput>> PayAsync(CreateOrderPaymentUseCaseInput? input = null, PaymentConfiguration? configuration = null)
        => BuildUseCase(configuration).ExecuteAsync(input ?? Input(), EventTestData.AuditableInfo());

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.pix.create.outcomes" && measurement.Value == 1).Outcome);

    private static string Code(MethodResult<CreateOrderPaymentOutput> result)
        => Assert.Single(result.Notifications).Code;

    [Fact]
    public async Task Given_PayableOrder_When_Paying_Should_ChargeTheFirstIntegrationAndSaveAPendingPix()
    {
        // Arrange & Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Output!.Reused);

        var payment = result.Output.Payment;

        Assert.Equal("PENDING", payment.Status);
        Assert.Equal("PIX", payment.Method);
        Assert.Equal(24000, payment.AmountCents);
        Assert.Equal(OrderTestData.NOW.AddMinutes(13), payment.ExpiresAt);
        Assert.NotNull(payment.Pix);
        Assert.StartsWith("00020101", payment.Pix.CopyPasteCode);

        var call = Assert.Single(_pixClient.Calls);

        Assert.Equal(KEY_A, call.ApiKey);
        Assert.Equal(24000, call.Request.AmountCents);
        Assert.Equal(780, call.Request.ExpiresInSeconds);
        Assert.Equal(payment.Id, call.Request.PaymentId);
        Assert.Equal(CODE, call.Request.OrderCode);
        Assert.Equal("Pedido NX7K2QH9MP - Festival Horizonte", call.Request.Description);
        Assert.Equal("Marina Souza", call.Request.CustomerName);
        Assert.Equal("52998224725", call.Request.CustomerTaxId);
        Assert.Equal("(81) 99999-0000", call.Request.CustomerCellphone);

        var saved = Assert.Single(_created);

        Assert.Equal(OrderTestData.INTEGRATION_A_ID, saved.IntegrationId);
        Assert.Equal(240.00m, saved.Amount);
        Assert.Equal("PENDING", saved.Status);
        Assert.Equal(EventTestData.PRODUCER_ID, saved.ProducerId);
        Assert.StartsWith("pix_char_", saved.ProviderReference);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_CREATED);
        Assert.Contains(_logger.Logs, log => log.EventId == 7101 && log.Level == LogLevel.Information);
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.provider.calls" && measurement.Value == 1);
        Assert.Contains(_measurements.Durations, duration => duration.Instrument == "ntix.payments.provider.duration");
        Assert.DoesNotContain(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.fallback.used" && measurement.Value == 1);
    }

    [Fact]
    public async Task Given_NoLogContainsSecrets_When_Paying_Should_KeepTheKeyAndTheCustomerDataOutOfTheLogs()
    {
        // Arrange & Act
        await PayAsync();

        // Assert
        Assert.All(_logger.Logs, log =>
        {
            Assert.DoesNotContain(KEY_A, log.Message);
            Assert.DoesNotContain("52998224725", log.Message);
            Assert.DoesNotContain("marina@exemplo.com", log.Message);
            Assert.DoesNotContain("00020101", log.Message);
        });
    }

    [Fact]
    public async Task Given_CustomerWithoutCellphone_When_Paying_Should_ChargeWithoutIt()
    {
        // Arrange
        _customerRepository.GetByIdAsync(OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(CustomerSessionTestData.NewCustomer(OrderTestData.CUSTOMER_ID, phone: null));

        // Act
        await PayAsync();

        // Assert
        Assert.Null(Assert.Single(_pixClient.Calls).Request.CustomerCellphone);
    }

    [Fact]
    public async Task Given_FirstIntegrationTimesOut_When_Paying_Should_FallBackToTheNextAndRecordIt()
    {
        // Arrange
        _pixClient.FailWith(KEY_A, PixChargeResultModel.Unavailable("timeout"));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([KEY_A, KEY_B], _pixClient.Calls.Select(call => call.ApiKey));
        Assert.Equal(OrderTestData.INTEGRATION_B_ID, Assert.Single(_created).IntegrationId);
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.fallback.used" && measurement.Value == 1);
        Assert.Contains(_logger.Logs, log => log.EventId == 7102 && log.Level == LogLevel.Warning && log.Message.Contains("timeout"));
    }

    [Theory]
    [InlineData("http_401")]
    [InlineData("http_429")]
    [InlineData("http_5xx")]
    [InlineData("http_4xx")]
    [InlineData("invalid_response")]
    public async Task Given_FirstIntegrationFails_When_Paying_Should_FallBackWhateverTheReason(string reason)
    {
        // Arrange
        _pixClient.FailWith(KEY_A, PixChargeResultModel.ProviderError(reason));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(OrderTestData.INTEGRATION_B_ID, Assert.Single(_created).IntegrationId);
    }

    [Fact]
    public async Task Given_EveryIntegrationFails_When_Paying_Should_ReportProvidersUnavailableAndSaveNoPayment()
    {
        // Arrange
        _pixClient.FailWith(KEY_A, PixChargeResultModel.ProviderError("http_5xx"));
        _pixClient.FailWith(KEY_B, PixChargeResultModel.ProviderError("http_5xx"));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE, Code(result));
        Assert.Empty(_created);
        Assert.Equal(2, _pixClient.Calls.Count);
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_PROVIDERS_FAILED);
        Assert.Contains(_logger.Logs, log => log.EventId == 7103 && log.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Given_MoreIntegrationsThanTheCap_When_AllFail_Should_TryOnlyTheConfiguredNumber()
    {
        // Arrange
        var third = Guid.NewGuid();
        var fourth = Guid.NewGuid();

        _integrationRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns([Integration(OrderTestData.INTEGRATION_A_ID, KEY_A), Integration(OrderTestData.INTEGRATION_B_ID, KEY_B), Integration(third, "chave-c"), Integration(fourth, "chave-d")]);
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Ranking((1, OrderTestData.INTEGRATION_A_ID), (2, OrderTestData.INTEGRATION_B_ID), (3, third), (4, fourth)));

        foreach (var key in new[] { KEY_A, KEY_B, "chave-c", "chave-d" })
            _pixClient.FailWith(key, PixChargeResultModel.Unavailable("timeout"));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(3, _pixClient.Calls.Count);
        Assert.DoesNotContain(_pixClient.Calls, call => call.ApiKey == "chave-d");
    }

    [Fact]
    public async Task Given_PendingValidPix_When_PayingAgain_Should_ReturnTheSameWithoutChargingAgain()
    {
        // Arrange
        var existing = OrderPaymentConversionsForTest(OrderTestData.NewPayment());

        _paymentRepository.GetLatestByOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(existing);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Output!.Reused);
        Assert.Equal(existing.Id, result.Output.Payment.Id);
        Assert.Equal(existing.PixCopyPaste, result.Output.Payment.Pix!.CopyPasteCode);
        Assert.Empty(_pixClient.Calls);
        Assert.Empty(_created);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_REUSED);
        Assert.Contains(_logger.Logs, log => log.EventId == 7104);
    }

    [Fact]
    public async Task Given_PendingPixThatExpired_When_Paying_Should_ExpireItAndChargeANewOne()
    {
        // Arrange: o Pix anterior venceu um segundo antes, mas a reserva do pedido ainda comporta outro.
        var expired = OrderPaymentConversionsForTest(OrderTestData.NewPayment(expiresAt: OrderTestData.NOW.AddSeconds(-1)));

        _paymentRepository.GetLatestByOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(expired);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Output!.Reused);
        Assert.Equal("EXPIRED", Assert.Single(_statusUpdates).Status);
        Assert.Equal(expired.Id, Assert.Single(_statusUpdates).Id);
        Assert.Single(_created);
    }

    [Fact]
    public async Task Given_AlreadyExpiredPix_When_Paying_Should_NotExpireItTwice()
    {
        // Arrange
        var paid = OrderPaymentConversionsForTest(OrderTestData.NewPayment(expiresAt: OrderTestData.NOW.AddSeconds(-1))) with { Status = "EXPIRED" };

        _paymentRepository.GetLatestByOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(paid);

        // Act
        await PayAsync();

        // Assert
        Assert.Empty(_statusUpdates);
        Assert.Single(_created);
    }

    [Fact]
    public async Task Given_PaidOrder_When_Paying_Should_ReportAlreadyPaid()
    {
        // Arrange
        _orderRepository.GetByCodeForUpdateAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns(_ => OrderTestData.NewModel() with { Status = "PAID", PaidAt = OrderTestData.NOW });

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_ALREADY_PAID_NOTIFICATION_CODE, Code(result));
        Assert.Empty(_pixClient.Calls);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_PAID);
    }

    [Fact]
    public async Task Given_ReservationPastItsEnd_When_Paying_Should_ReportExpiredAndRecordTheExpiry()
    {
        // Arrange: o pedido foi criado há 16 minutos, com um Pix ainda marcado como pendente.
        _timeProvider.Advance(TimeSpan.FromMinutes(16));
        _paymentRepository.GetLatestByOrderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OrderPaymentConversionsForTest(OrderTestData.NewPayment()));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_EXPIRED_NOTIFICATION_CODE, Code(result));
        Assert.Equal("EXPIRED", Assert.Single(_orderUpdates).Status);
        Assert.Equal("EXPIRED", Assert.Single(_statusUpdates).Status);
        Assert.Empty(_pixClient.Calls);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_EXPIRED);
    }

    [Fact]
    public async Task Given_ExpiredReservationWithoutPix_When_Paying_Should_OnlyExpireTheOrder()
    {
        // Arrange
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Act
        await PayAsync();

        // Assert
        Assert.Single(_orderUpdates);
        Assert.Empty(_statusUpdates);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(14, false)]
    public async Task Given_ReservationNearItsEnd_When_Paying_Should_NeedSevenMinutesLeft(int minutesSinceCreation, bool charges)
    {
        // Arrange
        _timeProvider.Advance(TimeSpan.FromMinutes(minutesSinceCreation));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(charges, result.IsSuccess);

        if (!charges)
        {
            Assert.Equal(CreateOrderPaymentUseCase.ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE, Code(result));
            Assert.Empty(_pixClient.Calls);
            ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_WINDOW_TOO_SHORT);
        }
    }

    [Fact]
    public async Task Given_NoIntegrationForPix_When_Paying_Should_ReturnTheRankingRefusalWithoutLockingTheOrder()
    {
        // Arrange
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<RankPaymentIntegrationsUseCaseOutput>.Error([Notification.Error(PaymentIntegrationRanking.PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE, "indisponível")]));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(PaymentIntegrationRanking.PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE, Code(result));
        await _orderRepository.DidNotReceive().GetByCodeForUpdateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_UNAVAILABLE);
    }

    [Fact]
    public async Task Given_MethodWithoutAdapter_When_Paying_Should_ReportUnavailableWithoutTouchingTheDatabase()
    {
        // Arrange & Act
        var result = await PayAsync(Input(method: "BOLETO"));

        // Assert
        Assert.Equal(PaymentIntegrationRanking.PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE, Code(result));
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_UNAVAILABLE);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pix")]
    [InlineData("DINHEIRO")]
    public async Task Given_InvalidMethod_When_Paying_Should_ReportInvalidMethod(string? method)
    {
        // Arrange & Act
        var result = await PayAsync(Input(method: method));

        // Assert
        Assert.Equal("PAYMENT_METHOD_INVALID", Code(result));
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Paying_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await BuildUseCase().ExecuteAsync(Input(), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Theory]
    [InlineData("NX123")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Given_CodeOutOfFormat_When_Paying_Should_ReportOrderNotFoundWithoutTouchingTheDatabase(string? code)
    {
        // Arrange & Act
        var result = await PayAsync(Input(code: code));

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_TokenWithoutCustomerId_When_Paying_Should_ReportOrderNotFound()
    {
        // Arrange & Act
        var result = await PayAsync(Input(customerId: "nao-e-guid"));

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_OrderOfAnotherCustomer_When_Paying_Should_ReportOrderNotFound()
    {
        // Arrange
        var other = Guid.NewGuid();

        _orderRepository.GetByCodeAsync(CODE, other, Arg.Any<CancellationToken>()).Returns((OrderModel?)null);

        // Act
        var result = await PayAsync(Input(customerId: other.ToString()));

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
        await _rankUseCase.DidNotReceive().ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_ORDER_NOT_FOUND);
    }

    [Fact]
    public async Task Given_OrderGoneBeforeTheLock_When_Paying_Should_ReportOrderNotFoundAndRollBack()
    {
        // Arrange
        _orderRepository.GetByCodeForUpdateAsync(CODE, OrderTestData.CUSTOMER_ID, Arg.Any<CancellationToken>()).Returns((OrderModel?)null);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_OrderWithoutItsCustomerOrEvent_When_Paying_Should_ReportOrderNotFound()
    {
        // Arrange
        _eventRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Infra.PostgreSql.Repositories.Events.Models.EventModel?)null);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
        Assert.Empty(_pixClient.Calls);
    }

    [Fact]
    public async Task Given_CustomerMissing_When_Paying_Should_ReportOrderNotFound()
    {
        // Arrange
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CustomerModel?)null);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Code(result));
    }

    [Fact]
    public async Task Given_ProviderWithoutAdapter_When_Paying_Should_SkipItAndUseTheNextIntegration()
    {
        // Arrange
        _rankUseCase.ExecuteAsync(Arg.Any<RankPaymentIntegrationsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<RankPaymentIntegrationsUseCaseOutput>.Success(new RankPaymentIntegrationsUseCaseOutput("PIX",
            [
                new RankedIntegrationOutput(1, Guid.NewGuid(), "OUTRO_FORNECEDOR"),
                new RankedIntegrationOutput(2, OrderTestData.INTEGRATION_B_ID, "ABACATEPAY"),
            ])));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(KEY_B, Assert.Single(_pixClient.Calls).ApiKey);
        Assert.Contains(_logger.Logs, log => log.EventId == 7102 && log.Message.Contains("no_adapter"));
    }

    [Fact]
    public async Task Given_IntegrationWithoutApiKey_When_Paying_Should_SkipItAndUseTheNextIntegration()
    {
        // Arrange
        _integrationRepository.GetByProducerAsync(EventTestData.PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns([Integration(OrderTestData.INTEGRATION_A_ID, apiKey: null), Integration(OrderTestData.INTEGRATION_B_ID, KEY_B)]);

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(KEY_B, Assert.Single(_pixClient.Calls).ApiKey);
        Assert.Contains(_logger.Logs, log => log.EventId == 7102 && log.Message.Contains("missing_credential"));
    }

    [Fact]
    public async Task Given_UnreadableCredential_When_Paying_Should_SkipItAndUseTheNextIntegration()
    {
        // Arrange: a primeira integração tem o valor cifrado adulterado.
        _cipher.Decrypt(Arg.Is<byte[]>(bytes => Encoding.UTF8.GetString(bytes) == KEY_A), Arg.Any<byte[]>()).Throws(new AuthenticationTagMismatchException());

        // Act
        var result = await PayAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(KEY_B, Assert.Single(_pixClient.Calls).ApiKey);
        Assert.Contains(_logger.Logs, log => log.EventId == 7102 && log.Message.Contains("credential_unreadable"));
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Paying_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => PayAsync());
        ThenOutcomeRecorded(CreateOrderPaymentDiagnostics.OUTCOME_ERROR);
        Assert.Contains(_logger.Logs, log => log.EventId == 7106 && log.Level == LogLevel.Error);
        Assert.Contains(_logger.Logs, log => log.EventId == 7107);
    }

    [Fact]
    public async Task Given_Cancellation_When_Paying_Should_ReturnCanceledNotification()
    {
        // Arrange
        _orderRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_CODE, Code(result));
    }

    private static PaymentModel OrderPaymentConversionsForTest(Domain.BoundedContexts.PaymentContext.Entities.Payment payment)
        => Application.UseCases.Base.Payments.OrderPaymentConversions.ToModel(payment);
}
