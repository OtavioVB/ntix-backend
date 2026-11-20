using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetCustomerOrder.Inputs;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Pedidos (REQ6) contra o PostgreSQL real: reserva, estoque, expiração, preço congelado, vínculo e concorrência.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class OrderUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string SET_ORDER_STATUS_SQL = "UPDATE orders.orders SET status = @status WHERE code = @code;";
    private const string EXPIRE_ORDER_SQL = "UPDATE orders.orders SET expires_at = now() - interval '1 minute' WHERE code = @code;";
    private const string CHANGE_PRICE_SQL = "UPDATE events.ticket_batch SET price = 150.00 WHERE id_ticket_batch = @id;";
    private const string COUNT_LINKS_SQL = "SELECT COUNT(*)::int FROM customers.producer_customer WHERE id_customer = @id_customer;";
    private const string COUNT_LINKS_WITH_PRODUCER_SQL = "SELECT COUNT(*)::int FROM customers.producer_customer WHERE id_customer = @id_customer AND id_producer = @id_producer;";
    private const string COUNT_ORDERS_SQL = "SELECT COUNT(*)::int FROM orders.orders WHERE id_customer = @id_customer;";

    private readonly PostgreSqlFixture _fixture;

    public OrderUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record GivenEvent(Guid ProducerId, string ProducerSlug, Guid EventId, string EventSlug, Guid BatchId);

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

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
            .ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), CustomerTestData.PASSWORD, null), CustomerTestData.NewAuditableInfo());

        Assert.True(created.IsSuccess);

        return (await _fixture.FindCustomerByEmailAsync(email))!.Id;
    }

    private async Task<GivenEvent> GivenPublishedEventAsync(int quantity = 10, string batchName = "Pista", long priceCents = 12000, (Guid Id, string Slug)? producer = null)
    {
        var (producerId, producerSlug) = producer ?? await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var startsAt = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            producerSlug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", "Recife", "PE", "FREE", null))).Output!;

        var batch = await GivenBatchAsync(producerSlug, created.Id, quantity, batchName, priceCents, DateTime.UtcNow.AddDays(-1), startsAt);

        await _fixture.ExecuteAsync(PUBLISH_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = created.Id });

        return new GivenEvent(producerId, producerSlug, created.Id, created.Slug, batch.Id);
    }

    private async Task<TicketBatchOutput> GivenBatchAsync(string producerSlug, Guid eventId, int quantity, string name, long priceCents, DateTime salesStartsAt, DateTime salesEndsAt)
    {
        var batch = await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            producerSlug, eventId.ToString(), TicketBatchConversions.BuildDetails(name, null, priceCents, quantity, Iso(salesStartsAt), Iso(salesEndsAt), "ACTIVE")));

        Assert.True(batch.IsSuccess);

        return batch.Output!;
    }

    private static CreateOrderUseCaseInput OrderOf(Guid customerId, GivenEvent given, int quantity, string channel = "HUB")
        => CreateOrderUseCaseInput.Create(customerId.ToString(), given.EventSlug, channel, [(given.BatchId.ToString(), quantity)]);

    private Task<MethodResult<OrderOutput>> CreateOrderAsync(CreateOrderUseCaseInput input)
        => ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(input);

    private Task<MethodResult<OrderOutput>> GetOrderAsync(Guid customerId, string code)
        => ExecuteAsync<GetCustomerOrderUseCaseInput, OrderOutput>(GetCustomerOrderUseCaseInput.Create(code, customerId.ToString()));

    private async Task<TicketBatchOutput> BatchAsync(GivenEvent given)
        => (await ExecuteAsync<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>(GetTicketBatchesUseCaseInput.Create(given.ProducerSlug, given.EventId.ToString()))).Output!.Batches.Single(batch => batch.Id == given.BatchId);

    private Task SetOrderStatusAsync(string code, string status)
        => _fixture.ExecuteAsync(SET_ORDER_STATUS_SQL, new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = code }, new NpgsqlParameter("@status", NpgsqlDbType.Varchar) { Value = status });

    private Task ExpireOrderAsync(string code)
        => _fixture.ExecuteAsync(EXPIRE_ORDER_SQL, new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = code });

    [Fact]
    public async Task Given_PublishedEvent_When_CreatingAnOrder_Should_PersistItReserveStockAndReturnIt()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();

        // Act
        var created = await CreateOrderAsync(OrderOf(customerId, given, 2));

        // Assert
        Assert.True(created.IsSuccess);

        var order = created.Output!;

        Assert.Equal("PENDING_PAYMENT", order.Status);
        Assert.Equal(24000, order.TotalCents);
        Assert.Equal(15, Math.Round((order.ExpiresAt - order.CreatedAt).TotalMinutes));
        Assert.StartsWith("NX", order.Code);

        var read = (await GetOrderAsync(customerId, order.Code)).Output!;

        Assert.Equal(order.CreatedAt, read.CreatedAt);
        Assert.Equal(order.ExpiresAt, read.ExpiresAt);
        Assert.Equal(order.Items, read.Items);
        Assert.Equal(given.EventSlug, read.Event.Slug);

        var batch = await BatchAsync(given);

        Assert.Equal(2, batch.ReservedQuantity);
        Assert.Equal(8, batch.AvailableQuantity);
    }

    [Fact]
    public async Task Given_PaidAndPendingOrders_When_SummingStock_Should_CountPaidAsSoldAndValidPendingAsReservedAndIgnoreExpired()
    {
        // Arrange
        var given = await GivenPublishedEventAsync(quantity: 20);
        var paid = (await CreateOrderAsync(OrderOf(await GivenCustomerAsync(), given, 3))).Output!;
        var pending = (await CreateOrderAsync(OrderOf(await GivenCustomerAsync(), given, 2))).Output!;
        var expired = (await CreateOrderAsync(OrderOf(await GivenCustomerAsync(), given, 4))).Output!;

        await SetOrderStatusAsync(paid.Code, "PAID");
        await ExpireOrderAsync(expired.Code);

        // Act
        var batch = await BatchAsync(given);

        // Assert
        Assert.Equal(3, batch.SoldQuantity);
        Assert.Equal(2, batch.ReservedQuantity);
        Assert.Equal(15, batch.AvailableQuantity);
        Assert.NotEqual(pending.Code, expired.Code);
    }

    [Fact]
    public async Task Given_ReservationPastItsEnd_When_Reading_Should_ShowExpiredAndReleaseTheStock()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var order = (await CreateOrderAsync(OrderOf(customerId, given, 2))).Output!;

        await ExpireOrderAsync(order.Code);

        // Act
        var read = (await GetOrderAsync(customerId, order.Code)).Output!;
        var batch = await BatchAsync(given);

        // Assert
        Assert.Equal("EXPIRED", read.Status);
        Assert.Equal(10, batch.AvailableQuantity);
    }

    [Fact]
    public async Task Given_StockLeftIsOne_When_AskingForTwo_Should_RefuseWithBatchNameAndRemaining()
    {
        // Arrange
        var given = await GivenPublishedEventAsync(quantity: 1);

        // Act
        var result = await CreateOrderAsync(OrderOf(await GivenCustomerAsync(), given, 2));

        // Assert
        var notification = Assert.Single(result.Notifications);

        Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, notification.Code);
        Assert.Contains("Pista", notification.Message);
        Assert.Contains("1 ingresso(s)", notification.Message);
    }

    [Fact]
    public async Task Given_RefusedOrder_When_Creating_Should_ReserveNothingNorLinkTheCustomer()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync(quantity: 1);

        // Act
        await CreateOrderAsync(OrderOf(customerId, given, 2));

        // Assert
        Assert.Equal(0, await _fixture.ScalarAsync<int>(COUNT_ORDERS_SQL, new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId }));
        Assert.Equal(0, await _fixture.ScalarAsync<int>(COUNT_LINKS_SQL, new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId }));
    }

    [Fact]
    public async Task Given_TheLastTicket_When_ManyCustomersAskAtTheSameTime_Should_ReserveItForExactlyOne()
    {
        // Arrange
        var given = await GivenPublishedEventAsync(quantity: 1);
        var customers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(customers.Select(customerId => CreateOrderAsync(OrderOf(customerId, given, 1))));

        // Assert
        Assert.Single(results, result => result.IsSuccess);

        var refused = results.Where(result => result.IsError).ToArray();

        Assert.Equal(7, refused.Length);
        Assert.All(refused, result => Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code));

        var batch = await BatchAsync(given);

        Assert.Equal(1, batch.ReservedQuantity);
        Assert.Equal(0, batch.AvailableQuantity);
    }

    [Fact]
    public async Task Given_OrdersCrossingTheSameBatchesInDifferentOrder_When_Concurrent_Should_NotDeadlockNorOversell()
    {
        // Arrange: dois lotes e pedidos que citam os lotes em ordens opostas; a trava por id evita o deadlock.
        var given = await GivenPublishedEventAsync(quantity: 20);
        var other = await GivenBatchAsync(given.ProducerSlug, given.EventId, 20, "VIP", 15000, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30));
        var customers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(customers.Select((customerId, index) => CreateOrderAsync(CreateOrderUseCaseInput.Create(
            customerId.ToString(), given.EventSlug, "HUB",
            index % 2 == 0
                ? [(given.BatchId.ToString(), 1), (other.Id.ToString(), 1)]
                : [(other.Id.ToString(), 1), (given.BatchId.ToString(), 1)]))));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(6, (await BatchAsync(given)).ReservedQuantity);
    }

    [Fact]
    public async Task Given_OrderCreated_When_ProducerChangesThePrice_Should_KeepThePriceOfTheOrder()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var order = (await CreateOrderAsync(OrderOf(customerId, given, 2))).Output!;

        await _fixture.ExecuteAsync(CHANGE_PRICE_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = given.BatchId });

        // Act
        var read = (await GetOrderAsync(customerId, order.Code)).Output!;

        // Assert
        Assert.Equal(24000, read.TotalCents);
        Assert.Equal(12000, Assert.Single(read.Items).UnitPriceCents);
        Assert.Equal(15000, (await BatchAsync(given)).PriceCents);
    }

    [Fact]
    public async Task Given_OrderOfAnotherCustomer_When_Reading_Should_ReportOrderNotFound()
    {
        // Arrange
        var owner = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var order = (await CreateOrderAsync(OrderOf(owner, given, 1))).Output!;

        // Act
        var result = await GetOrderAsync(await GivenCustomerAsync(), order.Code);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("ORDER_NOT_FOUND", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_BatchOfAnotherEvent_When_Creating_Should_ReportBatchNotFound()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var another = await GivenPublishedEventAsync();

        // Act
        var result = await CreateOrderAsync(CreateOrderUseCaseInput.Create(customerId.ToString(), given.EventSlug, "HUB", [(another.BatchId.ToString(), 1)]));

        // Assert
        Assert.Equal(Order.BATCH_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_BatchOutsideTheSalesWindow_When_Creating_Should_ReportNotOnSale()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var tomorrow = await GivenBatchAsync(given.ProducerSlug, given.EventId, 10, "2º lote", 15000, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(30));

        // Act
        var result = await CreateOrderAsync(CreateOrderUseCaseInput.Create(customerId.ToString(), given.EventSlug, "HUB", [(tomorrow.Id.ToString(), 1)]));

        // Assert
        Assert.Equal(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_DraftEvent_When_Creating_Should_ReportEventNotFound()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();

        await _fixture.ExecuteAsync("UPDATE events.event SET status = 'DRAFT' WHERE id_event = @id;", new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = given.EventId });

        // Act
        var result = await CreateOrderAsync(OrderOf(customerId, given, 1));

        // Assert
        Assert.Equal("EVENT_NOT_FOUND", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_SeveralOrdersWithTheSameProducer_When_Creating_Should_LinkTheCustomerOnlyOnce()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var first = await GivenPublishedEventAsync(producer: producer);
        var second = await GivenPublishedEventAsync(producer: producer);

        // Act
        await CreateOrderAsync(OrderOf(customerId, first, 1));
        await CreateOrderAsync(OrderOf(customerId, first, 1));
        await CreateOrderAsync(OrderOf(customerId, second, 1, channel: "STORE"));

        // Assert
        Assert.Equal(1, await _fixture.ScalarAsync<int>(COUNT_LINKS_SQL, new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId }));
        Assert.Equal(3, await _fixture.ScalarAsync<int>(COUNT_ORDERS_SQL, new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId }));
    }

    [Fact]
    public async Task Given_OrdersWithTwoProducers_When_Creating_Should_LinkTheCustomerToEachOne()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var first = await GivenPublishedEventAsync();
        var second = await GivenPublishedEventAsync();

        // Act
        await CreateOrderAsync(OrderOf(customerId, first, 1));
        await CreateOrderAsync(OrderOf(customerId, second, 1));

        // Assert
        Assert.Equal(2, await _fixture.ScalarAsync<int>(COUNT_LINKS_SQL, new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId }));
        Assert.Equal(1, await _fixture.ScalarAsync<int>(
            COUNT_LINKS_WITH_PRODUCER_SQL,
            new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId },
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = first.ProducerId }));
    }

    [Fact]
    public async Task Given_CodeAlreadyUsed_When_SavingAnotherOrder_Should_ReportItAndKeepTheTransactionUsable()
    {
        // Arrange
        var customerId = await GivenCustomerAsync();
        var given = await GivenPublishedEventAsync();
        var existing = (await CreateOrderAsync(OrderOf(customerId, given, 1))).Output!;

        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var current = (await GetOrderAsync(customerId, existing.Code)).Output!;
        var model = new OrderModel(
            Id: Guid.NewGuid(), Code: existing.Code, ProducerId: given.ProducerId, EventId: given.EventId, CustomerId: customerId,
            SalesChannel: "HUB", TotalAmount: 120.00m, Status: "PENDING_PAYMENT", ExpiresAt: DateTime.UtcNow.AddMinutes(15), PaidAt: null,
            CreatedAt: DateTime.UtcNow, CreatedCorrelationId: Guid.NewGuid(), UpdatedAt: DateTime.UtcNow, UpdatedCorrelationId: Guid.NewGuid(),
            Items: [new OrderItemModel(Guid.NewGuid(), given.BatchId, "Pista", 1, 120.00m)]);

        await unitOfWork.BeginTransactionAsync();

        // Act
        var result = await repository.CreateAsync(model);
        var stillReadable = await repository.GetByCodeAsync(existing.Code, customerId);

        await unitOfWork.RollbackAsync();

        // Assert
        Assert.Equal(OrderCreationResult.CodeAlreadyExists, result);
        Assert.Equal(current.Code, stillReadable!.Code);
    }

}
