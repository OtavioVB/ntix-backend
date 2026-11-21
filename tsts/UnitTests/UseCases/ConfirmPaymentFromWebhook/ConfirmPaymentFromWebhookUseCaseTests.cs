using Application.UseCases.Base.Payments;
using Application.UseCases.ConfirmPaymentFromWebhook;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.Http.Payments.AbacatePay;
using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using System.Text;
using UnitTests.Support;

namespace UnitTests.UseCases.ConfirmPaymentFromWebhook;

/// <summary>
/// Orquestração do webhook de pagamento (REQ8): cada desfecho com os repositórios substituídos, incluindo as recusas que não tocam o banco,
/// as duplicatas, a falha da fonte e o controle da transação.
/// </summary>
public sealed class ConfirmPaymentFromWebhookUseCaseTests : IDisposable
{
    private const string PROVIDER_REFERENCE = "pix_char_abc123";
    private const string API_KEY = "abc_dev_chave-de-teste";

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IPaymentRepository _paymentRepository = Substitute.For<IPaymentRepository>();
    private readonly IProducerIntegrationRepository _integrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ITicketRepository _ticketRepository = Substitute.For<ITicketRepository>();
    private readonly IOutboxRepository _outboxRepository = Substitute.For<IOutboxRepository>();
    private readonly IPaymentWebhookEventRepository _webhookEventRepository = Substitute.For<IPaymentWebhookEventRepository>();
    private readonly IPixChargeClient _pixClient = Substitute.For<IPixChargeClient>();
    private readonly ICredentialCipher _cipher = Substitute.For<ICredentialCipher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(OrderTestData.NOW));
    private readonly TestMeterFactory _meterFactory = new();
    private readonly ConfirmPaymentFromWebhookUseCase _useCase;

    private readonly AuditableInfoValueObject _auditableInfo = AuditableInfoValueObject.Build(
        ProducerSessionTestData.CORRELATION_ID, "abacatepay-webhook", "abacatepay", ProducerSessionTestData.REQUESTED_AT);

    public ConfirmPaymentFromWebhookUseCaseTests()
    {
        _pixClient.ProviderCode.Returns(AbacatePayClient.PROVIDER_CODE);
        _pixClient.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(new PixChargeStatusResultModel(true, "PAID", null));

        _cipher.Decrypt(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns(callInfo => (byte[])callInfo[0]);

        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(PaymentModel());
        _integrationRepository.GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([IntegrationModel()]);
        _orderRepository.GetByIdForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OrderTestData.NewModel());
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(CustomerSessionTestData.NewCustomer());
        _ticketRepository.CreateManyAsync(Arg.Any<IReadOnlyList<TicketModel>>(), Arg.Any<CancellationToken>()).Returns(TicketCreationResult.Created);
        _webhookEventRepository.CreateAsync(Arg.Any<PaymentWebhookEventModel>(), Arg.Any<CancellationToken>()).Returns(true);

        _useCase = new ConfirmPaymentFromWebhookUseCase(
            _orderRepository,
            _paymentRepository,
            _integrationRepository,
            _customerRepository,
            _ticketRepository,
            _outboxRepository,
            _webhookEventRepository,
            [_pixClient],
            _cipher,
            _unitOfWork,
            _timeProvider,
            new AbacatePayConfiguration { WebhookPublicKey = WebhookTestData.PUBLIC_KEY },
            new ConfirmPaymentFromWebhookDiagnostics(_meterFactory),
            new CapturingLogger<ConfirmPaymentFromWebhookUseCase>());
    }

    public void Dispose()
        => _meterFactory.Dispose();

    private static PaymentModel PaymentModel(decimal amount = 240m)
        => OrderPaymentConversions.ToModel(OrderTestData.NewPayment()) with { Amount = amount, ProviderReference = PROVIDER_REFERENCE };

    private static ProducerIntegrationModel IntegrationModel(bool withSecret = true)
    {
        var credentials = new List<ProducerIntegrationCredentialModel>
        {
            PaymentTestData.Credential("API_KEY", null, Encoding.UTF8.GetBytes(API_KEY)),
        };

        if (withSecret)
            credentials.Add(PaymentTestData.Credential("WEBHOOK_SECRET", null, Encoding.UTF8.GetBytes(WebhookTestData.WEBHOOK_SECRET)));

        return PaymentTestData.IntegrationModel(OrderTestData.INTEGRATION_A_ID, credentials: credentials.ToArray());
    }

    private Task<MethodResult> ExecuteAsync(byte[] body, string? signature = null, string? secret = WebhookTestData.WEBHOOK_SECRET, CancellationToken cancellationToken = default)
        => _useCase.ExecuteAsync(
            ConfirmPaymentFromWebhookUseCaseInput.Create(body, signature ?? WebhookTestData.Sign(body), secret),
            _auditableInfo,
            cancellationToken);

    private static byte[] Completed(string eventId = "log_1")
        => WebhookTestData.Envelope(eventId, PROVIDER_REFERENCE);

    [Fact]
    public async Task Given_SignatureDoesNotMatch_When_Executing_Should_RefuseWithoutTouchingTheDatabase()
    {
        // Arrange
        var body = Completed();

        // Act
        var result = await ExecuteAsync(body, signature: "YWFh");

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE);
        await _unitOfWork.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default);
        await _paymentRepository.DidNotReceiveWithAnyArgs().GetByProviderReferenceAsync(default!, default);
    }

    [Fact]
    public async Task Given_SignedBodyIsNotJson_When_Executing_Should_RefuseWithBodyInvalid()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("isto não é json");

        // Act
        var result = await ExecuteAsync(body);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_BODY_INVALID_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_CompletedEventWithoutTheChargeId_When_Executing_Should_RefuseWithBodyInvalid()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("{\"id\":\"log_1\",\"event\":\"transparent.completed\",\"data\":{}}");

        // Act
        var result = await ExecuteAsync(body);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_BODY_INVALID_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_EnvelopeIdAboveTheLimit_When_Executing_Should_RefuseWithBodyInvalid()
    {
        // Arrange
        var longId = new string('x', 129);
        var body = Encoding.UTF8.GetBytes($"{{\"id\":\"{longId}\",\"event\":\"transparent.completed\",\"data\":{{\"transparent\":{{\"id\":\"{PROVIDER_REFERENCE}\"}}}}}}");

        // Act
        var result = await ExecuteAsync(body);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_BODY_INVALID_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_EventThatDoesNotConfirmPayment_When_Executing_Should_RecordItAsIgnoredAndSucceed()
    {
        // Arrange
        var body = WebhookTestData.Envelope("log_refund", PROVIDER_REFERENCE, eventType: "transparent.refunded");

        // Act
        var result = await ExecuteAsync(body);

        // Assert
        Assert.True(result.IsSuccess);
        await _webhookEventRepository.Received(1).CreateAsync(Arg.Is<PaymentWebhookEventModel>(item => item.Outcome == "IGNORED" && item.PaymentId == null), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnknownChargeId_When_Executing_Should_RecordPaymentNotFoundAndRefuseWith404Code()
    {
        // Arrange
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns((PaymentModel?)null);

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.PAYMENT_NOT_FOUND_NOTIFICATION_CODE);
        await _webhookEventRepository.Received(1).CreateAsync(Arg.Is<PaymentWebhookEventModel>(item => item.Outcome == "PAYMENT_NOT_FOUND"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_WrongWebhookSecret_When_Executing_Should_RefuseWithSecretInvalid()
    {
        // Arrange (RN3)
        // Act
        var result = await ExecuteAsync(Completed(), secret: "outro-segredo");

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE);
        await _orderRepository.DidNotReceiveWithAnyArgs().GetByIdForUpdateAsync(default, default);
    }

    [Fact]
    public async Task Given_IntegrationWithoutWebhookSecret_When_Executing_Should_SkipTheSecretCheck()
    {
        // Arrange
        _integrationRepository.GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([IntegrationModel(withSecret: false)]);

        // Act
        var result = await ExecuteAsync(Completed(), secret: null);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Given_PaymentIntegrationMissing_When_Executing_Should_ReturnSourceUnavailable()
    {
        // Arrange
        _integrationRepository.GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_ApiKeyMissingFromIntegration_When_Executing_Should_ReturnSourceUnavailable()
    {
        // Arrange
        _integrationRepository.GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([PaymentTestData.IntegrationModel(OrderTestData.INTEGRATION_A_ID, credentials: [PaymentTestData.Credential("ACCOUNT_ID")])]);

        // Act
        var result = await ExecuteAsync(Completed(), secret: null);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_ApiKeyCannotBeDecrypted_When_Executing_Should_ReturnSourceUnavailableWithoutCallingTheSource()
    {
        // Arrange
        _cipher.Decrypt(Arg.Any<byte[]>(), Arg.Any<byte[]>()).Returns<byte[]>(_ => throw new AuthenticationTagMismatchException());
        _integrationRepository.GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([IntegrationModel(withSecret: false)]);

        // Act
        var result = await ExecuteAsync(Completed(), secret: null);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
        await _pixClient.DidNotReceiveWithAnyArgs().GetPixChargeStatusAsync(default!, default!, default);
    }

    [Fact]
    public async Task Given_NoAdapterForTheProvider_When_Executing_Should_ReturnSourceUnavailable()
    {
        // Arrange
        _pixClient.ProviderCode.Returns("OUTRO_FORNECEDOR");

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_SourceUnavailable_When_Executing_Should_ReturnSourceUnavailableWithoutRecording()
    {
        // Arrange (RN4): sem resposta da fonte, nada é gravado e o AbacatePay reenvia.
        _pixClient.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(PixChargeStatusResultModel.Unavailable("timeout"));

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
        await _webhookEventRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Given_SourceStillPending_When_Executing_Should_RecordNotPaidAtSourceAndNotConfirm()
    {
        // Arrange (RN4): o corpo diz PAID, mas a fonte diz PENDING.
        _pixClient.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(PixChargeStatusResultModel.Found("PENDING"));

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _webhookEventRepository.Received(1).CreateAsync(Arg.Is<PaymentWebhookEventModel>(item => item.Outcome == "NOT_PAID_AT_SOURCE"), Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceiveWithAnyArgs().CreateManyAsync(default!, default);
    }

    [Fact]
    public async Task Given_PaymentAmountDifferentFromTheOrder_When_Executing_Should_RecordAmountMismatchAndNotConfirm()
    {
        // Arrange (RN9): o Pix foi gerado por 250,00, mas o pedido soma 240,00 nos itens.
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(PaymentModel(amount: 250m));

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _webhookEventRepository.Received(1).CreateAsync(Arg.Is<PaymentWebhookEventModel>(item => item.Outcome == "AMOUNT_MISMATCH"), Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceiveWithAnyArgs().CreateManyAsync(default!, default);
        await _unitOfWork.Received().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_OrderAlreadyPaid_When_Executing_Should_RecordAlreadyPaidWithoutIssuingTickets()
    {
        // Arrange (RN6): pedido já pago, nada muda.
        _orderRepository.GetByIdForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(OrderTestData.NewModel() with { Status = "PAID" });

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _webhookEventRepository.Received(1).CreateAsync(Arg.Is<PaymentWebhookEventModel>(item => item.Outcome == "ALREADY_PAID"), Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceiveWithAnyArgs().CreateManyAsync(default!, default);
    }

    [Fact]
    public async Task Given_PaymentAlreadyPaid_When_Executing_Should_RecordAlreadyPaidWithoutIssuingTickets()
    {
        // Arrange
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).Returns(PaymentModel() with { Status = "PAID" });

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _ticketRepository.DidNotReceiveWithAnyArgs().CreateManyAsync(default!, default);
    }

    [Fact]
    public async Task Given_EnvelopeAlreadyRecorded_When_Executing_Should_RollbackWithoutIssuingTickets()
    {
        // Arrange (RN5): a corrida perdida, o insert do envelope volta falso.
        _webhookEventRepository.CreateAsync(Arg.Any<PaymentWebhookEventModel>(), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _ticketRepository.DidNotReceiveWithAnyArgs().CreateManyAsync(default!, default);
    }

    [Fact]
    public async Task Given_PixPaidAtSource_When_Executing_Should_ConfirmThePaymentIssueTicketsAndQueueOrderPaid()
    {
        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _paymentRepository.Received(1).UpdateStatusAsync(Arg.Is<PaymentModel>(item => item.Status == "PAID"), Arg.Any<CancellationToken>());
        await _orderRepository.Received(1).UpdateStatusAsync(Arg.Is<OrderModel>(item => item.Status == "PAID"), Arg.Any<CancellationToken>());
        await _ticketRepository.Received(1).CreateManyAsync(Arg.Is<IReadOnlyList<TicketModel>>(tickets => tickets.Count == 2), Arg.Any<CancellationToken>());
        await _outboxRepository.Received(1).AddAsync(Arg.Is<OutboxEventModel>(item => item.EventType == ConfirmPaymentFromWebhookUseCase.ORDER_PAID_EVENT_TYPE), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ReservationAlreadyExpired_When_Executing_Should_HonorThePaymentAndStillIssueTickets()
    {
        // Arrange (RN10): o pagamento chega 20 minutos depois da reserva de 15.
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _ticketRepository.Received(1).CreateManyAsync(Arg.Is<IReadOnlyList<TicketModel>>(tickets => tickets.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_TicketCodeAlreadyUsed_When_Executing_Should_RetryWithNewCodes()
    {
        // Arrange (RN7): o primeiro sorteio colide; o segundo grava.
        _ticketRepository.CreateManyAsync(Arg.Any<IReadOnlyList<TicketModel>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TicketCreationResult.CodeAlreadyExists), Task.FromResult(TicketCreationResult.Created));

        // Act
        var result = await ExecuteAsync(Completed());

        // Assert
        Assert.True(result.IsSuccess);
        await _ticketRepository.Received(2).CreateManyAsync(Arg.Any<IReadOnlyList<TicketModel>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_TicketCodeCollidesEveryTime_When_Executing_Should_FailAndRollback()
    {
        // Arrange
        _ticketRepository.CreateManyAsync(Arg.Any<IReadOnlyList<TicketModel>>(), Arg.Any<CancellationToken>()).Returns(TicketCreationResult.CodeAlreadyExists);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(Completed()));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_CancellationRequested_When_Executing_Should_ReturnCanceledNotification()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await ExecuteAsync(Completed(), cancellationToken: cancellation.Token);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.CONFIRM_PAYMENT_CANCELED_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_UnexpectedRepositoryFailure_When_Executing_Should_RollbackAndRethrow()
    {
        // Arrange
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("banco fora"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(Completed()));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RollbackFailsAfterFailure_When_Executing_Should_StillRethrowTheOriginalFailure()
    {
        // Arrange
        _paymentRepository.GetByProviderReferenceAsync(PROVIDER_REFERENCE, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha original"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("rollback falhou"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteAsync(Completed()));
        Assert.Equal("falha original", exception.Message);
    }
}
