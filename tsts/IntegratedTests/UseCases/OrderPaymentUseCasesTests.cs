using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetCustomerOrder.Inputs;
using Application.UseCases.GetOrderPaymentMethods.Inputs;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Infra.Http.Payments.Models;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Pagamento do pedido com Pix (REQ7) contra o PostgreSQL real, com o fornecedor falso: fallback, Pix único, janela, vencimento e integridade.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class OrderPaymentUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string COUNT_PAYMENTS_SQL = "SELECT COUNT(*)::int FROM payments.payment WHERE id_order = (SELECT id_order FROM orders.orders WHERE code = @code);";
    private const string COUNT_PENDING_SQL = "SELECT COUNT(*)::int FROM payments.payment WHERE status = 'PENDING' AND id_order = (SELECT id_order FROM orders.orders WHERE code = @code);";
    private const string PAYMENT_INTEGRATION_SQL = "SELECT id_producer_integration FROM payments.payment WHERE id_payment = @id;";
    private const string ORDER_STATUS_SQL = "SELECT status FROM orders.orders WHERE code = @code;";
    private const string PAYMENT_STATUS_SQL = "SELECT status FROM payments.payment WHERE id_payment = @id;";
    private const string SET_ORDER_EXPIRY_SQL = "UPDATE orders.orders SET expires_at = now() + make_interval(mins => @minutes) WHERE code = @code;";
    private const string SET_PAYMENT_EXPIRY_SQL = "UPDATE payments.payment SET expires_at = now() - interval '1 minute' WHERE id_payment = @id;";

    private readonly PostgreSqlFixture _fixture;

    public OrderPaymentUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record GivenOrder(Guid CustomerId, Guid ProducerId, string ProducerSlug, string Code, Guid IntegrationA, Guid IntegrationB, string KeyA, string KeyB);

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static NpgsqlParameter Code(string code)
        => new("@code", NpgsqlDbType.Varchar) { Value = code };

    private static NpgsqlParameter Id(Guid id)
        => new("@id", NpgsqlDbType.Uuid) { Value = id };

    private async Task<MethodResult<TOutput>> ExecuteAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<TInput, TOutput>>().ExecuteAsync(input, CustomerTestData.NewAuditableInfo());
    }

    private async Task<Guid> GivenCustomerAsync()
    {
        var email = CustomerTestData.NewEmail();

        using var scope = _fixture.Services.CreateScope();

        var created = await scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>()
            .ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), CustomerTestData.PASSWORD, "81999990000"), CustomerTestData.NewAuditableInfo());

        Assert.True(created.IsSuccess);

        return (await _fixture.FindCustomerByEmailAsync(email))!.Id;
    }

    private async Task<Guid> GivenIntegrationAsync(string slug, string apiKey)
    {
        var created = await ExecuteAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(CreateProducerIntegrationUseCaseInput.Create(
            slug, "ABACATEPAY", $"Conta {Guid.NewGuid():N}"[..20], new Dictionary<string, string?> { ["API_KEY"] = apiKey }));

        Assert.True(created.IsSuccess, string.Join(", ", created.Notifications.Select(notification => notification.Code)));

        return created.Output!.Id;
    }

    private async Task ReplaceRouteAsync(string slug, params Guid[] integrationIds)
    {
        var replaced = await ExecuteAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(
            ReplacePaymentRouteUseCaseInput.Create(slug, "PIX", integrationIds.Select(id => (string?)id.ToString()).ToArray()));

        Assert.True(replaced.IsSuccess);
    }

    /// <summary>Produtor com duas contas AbacatePay na rota de PIX (A e B), evento publicado e um pedido de 2 ingressos de R$ 120,00.</summary>
    private async Task<GivenOrder> GivenPayableOrderAsync()
    {
        var (producerId, slug) = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var keyA = $"abc_dev_{Guid.NewGuid():N}";
        var keyB = $"abc_dev_{Guid.NewGuid():N}";
        var integrationA = await GivenIntegrationAsync(slug, keyA);
        var integrationB = await GivenIntegrationAsync(slug, keyB);

        await ReplaceRouteAsync(slug, integrationA, integrationB);

        var startsAt = DateTime.UtcNow.Date.AddDays(60).AddHours(21);
        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            slug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", "Recife", "PE", "FREE", null))).Output!;
        var batch = (await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            slug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, 12000, 100, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")))).Output!;

        await _fixture.ExecuteAsync(PUBLISH_SQL, Id(created.Id));

        var customerId = await GivenCustomerAsync();
        var order = (await ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(
            CreateOrderUseCaseInput.Create(customerId.ToString(), created.Slug, "HUB", [(batch.Id.ToString(), 2)]))).Output!;

        return new GivenOrder(customerId, producerId, slug, order.Code, integrationA, integrationB, keyA, keyB);
    }

    private Task<MethodResult<CreateOrderPaymentOutput>> PayAsync(GivenOrder order, Guid? customerId = null)
        => ExecuteAsync<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput>(CreateOrderPaymentUseCaseInput.Create(order.Code, (customerId ?? order.CustomerId).ToString(), "PIX"));

    private async Task<OrderOutput> ReadOrderAsync(GivenOrder order)
        => (await ExecuteAsync<GetCustomerOrderUseCaseInput, OrderOutput>(GetCustomerOrderUseCaseInput.Create(order.Code, order.CustomerId.ToString()))).Output!;

    [Fact]
    public async Task Given_PayableOrder_When_Paying_Should_ChargeAccountAWithItsDecryptedKeyAndSaveThePix()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.True(result.IsSuccess);

        var payment = result.Output!.Payment;

        Assert.False(result.Output.Reused);
        Assert.Equal("PENDING", payment.Status);
        Assert.Equal(24000, payment.AmountCents);
        Assert.NotNull(payment.Pix);

        var call = Assert.Single(_fixture.PixClient.Calls, item => item.ApiKey == order.KeyA);

        Assert.Equal(24000, call.Request.AmountCents);
        Assert.Equal(order.Code, call.Request.OrderCode);
        Assert.Equal(payment.Id, call.Request.PaymentId);
        Assert.Equal("(81) 99999-0000", call.Request.CustomerCellphone);
        Assert.DoesNotContain(_fixture.PixClient.Calls, item => item.ApiKey == order.KeyB && item.Request.PaymentId == payment.Id);
        Assert.Equal(order.IntegrationA, await _fixture.ScalarAsync<Guid>(PAYMENT_INTEGRATION_SQL, Id(payment.Id)));
    }

    [Fact]
    public async Task Given_PixGenerated_When_ReadingTheOrder_Should_ShowThePaymentWithThePix()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var payment = (await PayAsync(order)).Output!.Payment;

        // Act
        var read = await ReadOrderAsync(order);

        // Assert
        Assert.Equal(payment.Id, read.Payment!.Id);
        Assert.Equal("PENDING", read.Payment.Status);
        Assert.Equal(payment.Pix!.CopyPasteCode, read.Payment.Pix!.CopyPasteCode);
        Assert.Equal(payment.ExpiresAt, read.Payment.ExpiresAt);
    }

    [Fact]
    public async Task Given_PixAlreadyGenerated_When_PayingAgain_Should_ReturnTheSameWithoutChargingAgain()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var first = (await PayAsync(order)).Output!;

        // Act
        var second = (await PayAsync(order)).Output!;

        // Assert
        Assert.True(second.Reused);
        Assert.Equal(first.Payment.Id, second.Payment.Id);
        Assert.Equal(first.Payment.Pix!.CopyPasteCode, second.Payment.Pix!.CopyPasteCode);
        Assert.Equal(1, _fixture.PixClient.Calls.Count(call => call.Request.OrderCode == order.Code));
        Assert.Equal(1, await _fixture.ScalarAsync<int>(COUNT_PAYMENTS_SQL, Code(order.Code)));
    }

    [Fact]
    public async Task Given_ManyClicksAtTheSameTime_When_Paying_Should_GenerateOnePixAndChargeOnce()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PayAsync(order)));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Single(results, result => !result.Output!.Reused);
        Assert.Single(results.Select(result => result.Output!.Payment.Id).Distinct());
        Assert.Equal(1, _fixture.PixClient.Calls.Count(call => call.Request.OrderCode == order.Code));
        Assert.Equal(1, await _fixture.ScalarAsync<int>(COUNT_PAYMENTS_SQL, Code(order.Code)));
    }

    [Fact]
    public async Task Given_AccountAFails_When_Paying_Should_FallBackToAccountBAndRecordItAsTheProcessor()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        _fixture.PixClient.FailWith(order.KeyA, PixChargeResultModel.Unavailable("timeout"));

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(order.IntegrationB, await _fixture.ScalarAsync<Guid>(PAYMENT_INTEGRATION_SQL, Id(result.Output!.Payment.Id)));
        Assert.Equal(2, _fixture.PixClient.Calls.Count(call => call.Request.OrderCode == order.Code));
    }

    [Fact]
    public async Task Given_EveryAccountFails_When_Paying_Should_ReportProvidersUnavailableAndSaveNoPayment()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        _fixture.PixClient.FailWith(order.KeyA, PixChargeResultModel.ProviderError("http_5xx"));
        _fixture.PixClient.FailWith(order.KeyB, PixChargeResultModel.ProviderError("http_5xx"));

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Equal(0, await _fixture.ScalarAsync<int>(COUNT_PAYMENTS_SQL, Code(order.Code)));
    }

    [Fact]
    public async Task Given_RouteChangedAfterThePix_When_Reading_Should_KeepTheIntegrationThatProcessedIt()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var payment = (await PayAsync(order)).Output!.Payment;

        await ReplaceRouteAsync(order.ProducerSlug, order.IntegrationB);

        // Act
        var integrationId = await _fixture.ScalarAsync<Guid>(PAYMENT_INTEGRATION_SQL, Id(payment.Id));

        // Assert
        Assert.Equal(order.IntegrationA, integrationId);
    }

    [Fact]
    public async Task Given_ReservationEnded_When_Paying_Should_ReportExpiredAndRecordTheExpiryOfOrderAndPix()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var payment = (await PayAsync(order)).Output!.Payment;

        await _fixture.ExecuteAsync(SET_ORDER_EXPIRY_SQL, Code(order.Code), new NpgsqlParameter("@minutes", NpgsqlDbType.Integer) { Value = -1 });

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_EXPIRED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Equal("EXPIRED", await _fixture.ScalarAsync<string>(ORDER_STATUS_SQL, Code(order.Code)));
        Assert.Equal("EXPIRED", await _fixture.ScalarAsync<string>(PAYMENT_STATUS_SQL, Id(payment.Id)));
    }

    [Fact]
    public async Task Given_ReservationNearItsEnd_When_Paying_Should_ReportWindowTooShort()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        await _fixture.ExecuteAsync(SET_ORDER_EXPIRY_SQL, Code(order.Code), new NpgsqlParameter("@minutes", NpgsqlDbType.Integer) { Value = 6 });

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Equal(0, await _fixture.ScalarAsync<int>(COUNT_PAYMENTS_SQL, Code(order.Code)));
    }

    [Fact]
    public async Task Given_PixThatExpired_When_PayingAgain_Should_ExpireItAndGenerateANewOne()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var first = (await PayAsync(order)).Output!.Payment;

        await _fixture.ExecuteAsync(SET_PAYMENT_EXPIRY_SQL, Id(first.Id));

        // Act
        var second = (await PayAsync(order)).Output!;

        // Assert
        Assert.False(second.Reused);
        Assert.NotEqual(first.Id, second.Payment.Id);
        Assert.Equal("EXPIRED", await _fixture.ScalarAsync<string>(PAYMENT_STATUS_SQL, Id(first.Id)));
        Assert.Equal(2, await _fixture.ScalarAsync<int>(COUNT_PAYMENTS_SQL, Code(order.Code)));
        Assert.Equal(1, await _fixture.ScalarAsync<int>(COUNT_PENDING_SQL, Code(order.Code)));
    }

    [Fact]
    public async Task Given_PaidOrder_When_Paying_Should_ReportAlreadyPaid()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        await _fixture.ExecuteAsync("UPDATE orders.orders SET status = 'PAID', paid_at = now() WHERE code = @code;", Code(order.Code));

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_ALREADY_PAID_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_OrderOfAnotherCustomer_When_Paying_Should_ReportOrderNotFound()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        // Act
        var result = await PayAsync(order, customerId: await GivenCustomerAsync());

        // Assert
        Assert.Equal(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Empty(_fixture.PixClient.Calls.Where(call => call.Request.OrderCode == order.Code));
    }

    [Fact]
    public async Task Given_EmptyPixRoute_When_Paying_Should_ReportMethodUnavailable()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        await ReplaceRouteAsync(order.ProducerSlug);

        // Act
        var result = await PayAsync(order);

        // Assert
        Assert.Equal("PAYMENT_METHOD_UNAVAILABLE", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_PixRoute_When_ListingMethods_Should_OfferPixAndNothingWithoutTheRoute()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();

        // Act
        var withRoute = await ExecuteAsync<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput>(GetOrderPaymentMethodsUseCaseInput.Create(order.Code, order.CustomerId.ToString()));

        await ReplaceRouteAsync(order.ProducerSlug);

        var withoutRoute = await ExecuteAsync<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput>(GetOrderPaymentMethodsUseCaseInput.Create(order.Code, order.CustomerId.ToString()));

        // Assert
        Assert.Equal("PIX", Assert.Single(withRoute.Output!.Methods).Method);
        Assert.Empty(withoutRoute.Output!.Methods);
    }

    [Fact]
    public async Task Given_TwoPendingPixForTheSameOrder_When_Saving_Should_RefuseTheSecondByTheUniqueIndex()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var first = (await PayAsync(order)).Output!.Payment;

        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var orderId = await _fixture.ScalarAsync<Guid>("SELECT id_order FROM orders.orders WHERE code = @code;", Code(order.Code));

        await unitOfWork.BeginTransactionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => repository.CreateAsync(Model(orderId, order.ProducerId, order.IntegrationA, "pix_char_" + Guid.NewGuid().ToString("N"))));

        await unitOfWork.RollbackAsync();

        Assert.Equal("ux_payment_order_pending", exception.ConstraintName);
        Assert.NotEqual(Guid.Empty, first.Id);
    }

    [Fact]
    public async Task Given_IntegrationOfAnotherProducer_When_Saving_Should_RefuseByTheCompositeForeignKey()
    {
        // Arrange
        var order = await GivenPayableOrderAsync();
        var other = await GivenPayableOrderAsync();

        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var orderId = await _fixture.ScalarAsync<Guid>("SELECT id_order FROM orders.orders WHERE code = @code;", Code(order.Code));

        await unitOfWork.BeginTransactionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => repository.CreateAsync(Model(orderId, order.ProducerId, other.IntegrationA, "pix_char_" + Guid.NewGuid().ToString("N"))));

        await unitOfWork.RollbackAsync();

        Assert.Equal("fk_payment_producer_integration", exception.ConstraintName);
    }

    [Fact]
    public async Task Given_SameProviderReferenceTwice_When_Saving_Should_RefuseByTheUniqueIndex()
    {
        // Arrange
        var first = await GivenPayableOrderAsync();
        var second = await GivenPayableOrderAsync();
        var reference = (await PayAsync(first)).Output!.Payment;
        var providerReference = await _fixture.ScalarAsync<string>("SELECT provider_reference FROM payments.payment WHERE id_payment = @id;", Id(reference.Id));

        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var orderId = await _fixture.ScalarAsync<Guid>("SELECT id_order FROM orders.orders WHERE code = @code;", Code(second.Code));

        await unitOfWork.BeginTransactionAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PostgresException>(() => repository.CreateAsync(Model(orderId, second.ProducerId, second.IntegrationA, providerReference)));

        await unitOfWork.RollbackAsync();

        Assert.Equal("ux_payment_provider_reference", exception.ConstraintName);
    }

    private static PaymentModel Model(Guid orderId, Guid producerId, Guid integrationId, string providerReference)
        => new(
            Id: Guid.NewGuid(), OrderId: orderId, ProducerId: producerId, IntegrationId: integrationId, Method: "PIX", Amount: 240.00m, Status: "PENDING",
            ProviderReference: providerReference, PixCopyPaste: "00020101", PixQrCodeImage: "data:image/png;base64,AAAA", ExpiresAt: DateTime.UtcNow.AddMinutes(10),
            PaidAt: null, CreatedAt: DateTime.UtcNow, CreatedCorrelationId: Guid.NewGuid(), UpdatedAt: DateTime.UtcNow, UpdatedCorrelationId: Guid.NewGuid());
}
