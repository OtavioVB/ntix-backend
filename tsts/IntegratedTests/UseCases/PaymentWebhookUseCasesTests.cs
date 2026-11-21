using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.ConfirmPaymentFromWebhook;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using UnitTests.Support;

namespace IntegratedTests.UseCases;

/// <summary>
/// Efetivação do pagamento pelo webhook (REQ8) contra o PostgreSQL real, com o fornecedor falso: confirmação, ingressos, evento
/// OrderPaid, deduplicação, recusas e falha da fonte.
/// </summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class PaymentWebhookUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string PROVIDER_REFERENCE_SQL = "SELECT p.provider_reference FROM payments.payment p JOIN orders.orders o ON o.id_order = p.id_order WHERE o.code = @code;";
    private const string ORDER_ID_SQL = "SELECT id_order FROM orders.orders WHERE code = @code;";
    private const string ORDER_STATUS_SQL = "SELECT status FROM orders.orders WHERE code = @code;";
    private const string TICKET_COUNT_SQL = "SELECT COUNT(*)::int FROM orders.ticket t JOIN orders.orders o ON o.id_order = t.id_order WHERE o.code = @code;";
    private const string WEBHOOK_OUTCOME_SQL = "SELECT COUNT(*)::int FROM payments.payment_webhook_event WHERE provider_event_id = @event_id AND outcome = @outcome;";

    private readonly PostgreSqlFixture _fixture;

    public PaymentWebhookUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record PendingPix(Guid CustomerId, string Code, string ProviderReference, Guid OrderId);

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static NpgsqlParameter Text(string name, string value)
        => new(name, NpgsqlDbType.Varchar) { Value = value };

    private static NpgsqlParameter Id(Guid id)
        => new("@id", NpgsqlDbType.Uuid) { Value = id };

    private async Task<MethodResult<TOutput>> ExecuteAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<TInput, TOutput>>().ExecuteAsync(input, CustomerTestData.NewAuditableInfo());
    }

    private async Task<MethodResult> WebhookAsync(byte[] body, string? signature = null, string? secret = WebhookTestData.WEBHOOK_SECRET)
    {
        using var scope = _fixture.Services.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<ConfirmPaymentFromWebhookUseCaseInput>>();

        return await useCase.ExecuteAsync(
            ConfirmPaymentFromWebhookUseCaseInput.Create(body, signature ?? WebhookTestData.Sign(body), secret),
            CustomerTestData.NewAuditableInfo());
    }

    /// <summary>Pedido de 2 ingressos com o Pix gerado (REQ7) e a cobrança pronta para ser paga no fornecedor falso.</summary>
    private async Task<PendingPix> GivenPendingPixAsync()
    {
        var (producerId, slug) = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integration = await ExecuteAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(CreateProducerIntegrationUseCaseInput.Create(
            slug, "ABACATEPAY", $"Conta {Guid.NewGuid():N}"[..20], new Dictionary<string, string?> { ["API_KEY"] = $"abc_dev_{Guid.NewGuid():N}", ["WEBHOOK_SECRET"] = WebhookTestData.WEBHOOK_SECRET }));

        Assert.True(integration.IsSuccess, string.Join(", ", integration.Notifications.Select(notification => notification.Code)));

        var replaced = await ExecuteAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(
            ReplacePaymentRouteUseCaseInput.Create(slug, "PIX", [integration.Output!.Id.ToString()]));

        Assert.True(replaced.IsSuccess);

        var startsAt = DateTime.UtcNow.Date.AddDays(60).AddHours(21);
        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            slug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", "Recife", "PE", "FREE", null))).Output!;
        var batch = (await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            slug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, 12000, 100, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")))).Output!;

        await _fixture.ExecuteAsync(PUBLISH_SQL, Id(created.Id));

        var email = CustomerTestData.NewEmail();

        using (var scope = _fixture.Services.CreateScope())
        {
            var customer = await scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>()
                .ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), CustomerTestData.PASSWORD, "81999990000"), CustomerTestData.NewAuditableInfo());

            Assert.True(customer.IsSuccess);
        }

        var customerId = (await _fixture.FindCustomerByEmailAsync(email))!.Id;
        var order = (await ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(
            CreateOrderUseCaseInput.Create(customerId.ToString(), created.Slug, "HUB", [(batch.Id.ToString(), 2)]))).Output!;

        var pix = await ExecuteAsync<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput>(
            CreateOrderPaymentUseCaseInput.Create(order.Code, customerId.ToString(), "PIX"));

        Assert.True(pix.IsSuccess);

        var reference = await _fixture.ScalarAsync<string>(PROVIDER_REFERENCE_SQL, Text("@code", order.Code));
        var orderId = await _fixture.ScalarAsync<Guid>(ORDER_ID_SQL, Text("@code", order.Code));

        return new PendingPix(customerId, order.Code, reference, orderId);
    }

    [Fact]
    public async Task Given_PixPaidAtSource_When_WebhookArrives_Should_ConfirmOrderIssueTicketsAndQueueOrderPaid()
    {
        // Arrange
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.SetStatus(pix.ProviderReference, "PAID");
        var eventId = $"log_{Guid.NewGuid():N}";

        // Act
        var result = await WebhookAsync(WebhookTestData.Envelope(eventId, pix.ProviderReference));

        // Assert
        Assert.True(result.IsSuccess, string.Join(", ", result.Notifications.Select(notification => notification.Code)));
        Assert.Equal("PAID", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Text("@code", pix.Code)));
        Assert.Equal(2, await _fixture.ScalarAsync<int>(TICKET_COUNT_SQL, Text("@code", pix.Code)));

        var outbox = await _fixture.FindOutboxEventsByAggregateAsync(pix.OrderId);

        Assert.Single(outbox, item => item.EventType == ConfirmPaymentFromWebhookUseCase.ORDER_PAID_EVENT_TYPE);
    }

    [Fact]
    public async Task Given_ConfirmedOrder_When_SameEnvelopeArrivesAgain_Should_NotIssueTicketsTwice()
    {
        // Arrange
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.SetStatus(pix.ProviderReference, "PAID");
        var body = WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", pix.ProviderReference);

        await WebhookAsync(body);

        // Act
        var second = await WebhookAsync(body);

        // Assert (RN5, RN6): a repetição responde sucesso e não emite de novo.
        Assert.True(second.IsSuccess);
        Assert.Equal(2, await _fixture.ScalarAsync<int>(TICKET_COUNT_SQL, Text("@code", pix.Code)));
    }

    [Fact]
    public async Task Given_PixPendingAtSource_When_WebhookArrives_Should_NotConfirmAndRecordIt()
    {
        // Arrange (RN4): o corpo diz PAID, mas a fonte diz PENDING: só a fonte confirma.
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.SetStatus(pix.ProviderReference, "PENDING");
        var eventId = $"log_{Guid.NewGuid():N}";

        // Act
        var result = await WebhookAsync(WebhookTestData.Envelope(eventId, pix.ProviderReference));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PENDING_PAYMENT", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Text("@code", pix.Code)));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(TICKET_COUNT_SQL, Text("@code", pix.Code)));
        Assert.Equal(1, await _fixture.ScalarAsync<int>(WEBHOOK_OUTCOME_SQL, Text("@event_id", eventId), Text("@outcome", "NOT_PAID_AT_SOURCE")));
    }

    [Fact]
    public async Task Given_SourceUnavailable_When_WebhookArrives_Should_ReturnSourceUnavailableWithoutConfirming()
    {
        // Arrange
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.FailStatusQuery(pix.ProviderReference, "timeout");

        // Act
        var result = await WebhookAsync(WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", pix.ProviderReference));

        // Assert: 503 no webhook, para o AbacatePay reenviar; nada é gravado.
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);
        Assert.Equal("PENDING_PAYMENT", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Text("@code", pix.Code)));
    }

    [Fact]
    public async Task Given_SignatureDoesNotMatch_When_WebhookArrives_Should_RefuseBeforeReadingTheBody()
    {
        // Arrange
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.SetStatus(pix.ProviderReference, "PAID");

        // Act
        var result = await WebhookAsync(WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", pix.ProviderReference), signature: "YWFh");

        // Assert (RN1): 401, e o pedido segue pendente.
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE);
        Assert.Equal("PENDING_PAYMENT", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Text("@code", pix.Code)));
    }

    [Fact]
    public async Task Given_WrongWebhookSecret_When_WebhookArrives_Should_RefuseWith401()
    {
        // Arrange (RN3): a integração tem WEBHOOK_SECRET; o header errado não passa.
        var pix = await GivenPendingPixAsync();
        _fixture.PixClient.SetStatus(pix.ProviderReference, "PAID");
        var body = WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", pix.ProviderReference);

        // Act
        var result = await WebhookAsync(body, secret: "outro-segredo");

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE);
        Assert.Equal("PENDING_PAYMENT", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Text("@code", pix.Code)));
    }

    [Fact]
    public async Task Given_UnknownPixId_When_WebhookArrives_Should_ReturnPaymentNotFound()
    {
        // Act
        var result = await WebhookAsync(WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", $"pix_char_inexistente_{Guid.NewGuid():N}"));

        // Assert (RN2): 404, sem reenvio.
        Assert.Contains(result.Notifications, notification => notification.Code == ConfirmPaymentFromWebhookUseCase.PAYMENT_NOT_FOUND_NOTIFICATION_CODE);
    }
}
