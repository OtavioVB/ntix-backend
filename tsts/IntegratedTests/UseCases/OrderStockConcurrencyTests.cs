using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>
/// Controle de concorrência do estoque (REQ6, RN8) contra o PostgreSQL real, em duas camadas: a trava <c>FOR UPDATE</c> do caso de uso
/// e a barreira do banco (<c>tr_order_item_batch_stock</c>), que vale mesmo para quem gravar sem passar pela trava.
/// </summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class OrderStockConcurrencyTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string GUARD_CONSTRAINT = "ck_order_item_batch_stock";

    private const string INSERT_ORDER_SQL = """
        INSERT INTO orders.orders (id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
                                   created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_order, @code, @id_producer, @id_event, @id_customer, 'HUB', @total, @status, @expires_at, NULL, now(), gen_random_uuid(), now(), gen_random_uuid());
        """;

    private const string INSERT_ITEM_SQL = """
        INSERT INTO orders.order_item (id_order_item, id_order, id_ticket_batch, batch_name, quantity, unit_price)
        VALUES (gen_random_uuid(), @id_order, @id_ticket_batch, 'Pista', @quantity, 120.00);
        """;

    private const string USED_SQL = """
        SELECT COALESCE(SUM(item.quantity), 0)::int
        FROM orders.order_item item
        JOIN orders.orders o ON o.id_order = item.id_order
        WHERE item.id_ticket_batch = @batch AND (o.status = 'PAID' OR (o.status = 'PENDING_PAYMENT' AND o.expires_at > now()));
        """;

    private readonly PostgreSqlFixture _fixture;

    public OrderStockConcurrencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record GivenStock(Guid ProducerId, Guid EventId, string EventSlug, Guid BatchId, Guid CustomerId, int Quantity);

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

    private async Task<GivenStock> GivenBatchWithStockAsync(int quantity)
    {
        var (producerId, slug) = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var startsAt = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            slug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", "Recife", "PE", "FREE", null))).Output!;
        var batch = (await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            slug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, 12000, quantity, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")))).Output!;

        await _fixture.ExecuteAsync(PUBLISH_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = created.Id });

        return new GivenStock(producerId, created.Id, created.Slug, batch.Id, await GivenCustomerAsync(), quantity);
    }

    private Task<int> UsedAsync(GivenStock stock)
        => _fixture.ScalarAsync<int>(USED_SQL, new NpgsqlParameter("@batch", NpgsqlDbType.Uuid) { Value = stock.BatchId });

    /// <summary>Pedido e item gravados direto no banco, sem trava nenhuma: o que um código descuidado faria.</summary>
    private static async Task InsertOrderWithItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GivenStock stock, int quantity, string status = "PENDING_PAYMENT", bool expired = false)
    {
        var orderId = Guid.NewGuid();

        await using (var order = new NpgsqlCommand(INSERT_ORDER_SQL, connection, transaction))
        {
            order.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;
            order.Parameters.Add("@code", NpgsqlDbType.Varchar, 10).Value = OrderCodeValueObject.Generate().Value;
            order.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = stock.ProducerId;
            order.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = stock.EventId;
            order.Parameters.Add("@id_customer", NpgsqlDbType.Uuid).Value = stock.CustomerId;
            order.Parameters.Add("@total", NpgsqlDbType.Numeric).Value = quantity * 120.00m;
            order.Parameters.Add("@status", NpgsqlDbType.Varchar, 32).Value = status;
            order.Parameters.Add("@expires_at", NpgsqlDbType.TimestampTz).Value = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(15);

            await order.ExecuteNonQueryAsync();
        }

        await using var item = new NpgsqlCommand(INSERT_ITEM_SQL, connection, transaction);

        item.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;
        item.Parameters.Add("@id_ticket_batch", NpgsqlDbType.Uuid).Value = stock.BatchId;
        item.Parameters.Add("@quantity", NpgsqlDbType.Integer).Value = quantity;

        await item.ExecuteNonQueryAsync();
    }

    /// <summary>Grava e confirma uma transação com um pedido; devolve a exceção da confirmação, se houver.</summary>
    private async Task<PostgresException?> CommitRawOrderAsync(GivenStock stock, int quantity, string status = "PENDING_PAYMENT", bool expired = false)
    {
        await using var connection = await _fixture.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await InsertOrderWithItemAsync(connection, transaction, stock, quantity, status, expired);

        try
        {
            await transaction.CommitAsync();

            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }

    private Task<MethodResult<OrderOutput>> CreateOrderAsync(GivenStock stock, Guid customerId, int quantity)
        => ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(CreateOrderUseCaseInput.Create(customerId.ToString(), stock.EventSlug, "HUB", [(stock.BatchId.ToString(), quantity)]));

    [Fact]
    public async Task Given_ManyBuyersForFewTickets_When_ReservingTogether_Should_ReserveExactlyTheStock()
    {
        // Arrange
        var stock = await GivenBatchWithStockAsync(quantity: 5);
        var customers = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(customers.Select(customerId => CreateOrderAsync(stock, customerId, 1)));

        // Assert
        Assert.Equal(5, results.Count(result => result.IsSuccess));
        Assert.All(results.Where(result => result.IsError), result => Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code));
        Assert.Equal(5, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_OrdersOfSeveralTickets_When_ReservingTogether_Should_NeverPassTheStock()
    {
        // Arrange: 12 compradores pedem 2 cada um para 7 ingressos: cabem 3 pedidos (6 ingressos).
        var stock = await GivenBatchWithStockAsync(quantity: 7);
        var customers = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(customers.Select(customerId => CreateOrderAsync(stock, customerId, 2)));

        // Assert
        Assert.Equal(3, results.Count(result => result.IsSuccess));
        Assert.Equal(6, await UsedAsync(stock));
        Assert.True(await UsedAsync(stock) <= stock.Quantity);
    }

    [Fact]
    public async Task Given_ExpiredReservationsOfTheSameBatch_When_ReservingTogether_Should_ReleaseTheirStock()
    {
        // Arrange: o lote inteiro está reservado, mas as reservas venceram.
        var stock = await GivenBatchWithStockAsync(quantity: 4);

        Assert.Null(await CommitRawOrderAsync(stock, 4, expired: true));

        var customers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(customers.Select(customerId => CreateOrderAsync(stock, customerId, 1)));

        // Assert
        Assert.Equal(4, results.Count(result => result.IsSuccess));
        Assert.Equal(4, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_OneTransaction_When_ItReservesMoreThanTheStock_Should_BeRefusedByTheDatabaseAtCommit()
    {
        // Arrange
        var stock = await GivenBatchWithStockAsync(quantity: 5);

        // Act
        var exception = await CommitRawOrderAsync(stock, 6);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(GUARD_CONSTRAINT, exception.ConstraintName);
        Assert.Equal(0, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_ExactlyTheStock_When_Reserving_Should_BeAccepted()
    {
        // Arrange
        var stock = await GivenBatchWithStockAsync(quantity: 5);

        // Act
        var exception = await CommitRawOrderAsync(stock, 5);

        // Assert
        Assert.Null(exception);
        Assert.Equal(5, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_TwoTransactionsThatSkipTheApplicationLock_When_BothCommit_Should_LetOnlyOneThrough()
    {
        // Arrange: cada uma reserva 3 de 5 sem travar nada; as duas gravam antes de qualquer uma confirmar, e nenhuma enxerga a outra.
        var stock = await GivenBatchWithStockAsync(quantity: 5);

        await using var connectionA = await _fixture.OpenConnectionAsync();
        await using var connectionB = await _fixture.OpenConnectionAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await using var transactionB = await connectionB.BeginTransactionAsync();

        await InsertOrderWithItemAsync(connectionA, transactionA, stock, 3);
        await InsertOrderWithItemAsync(connectionB, transactionB, stock, 3);

        // Act
        var commits = await Task.WhenAll(TryCommitAsync(transactionA), TryCommitAsync(transactionB));

        // Assert
        Assert.Single(commits, exception => exception is null);

        var refused = Assert.Single(commits, exception => exception is not null)!;

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(GUARD_CONSTRAINT, refused.ConstraintName);
        Assert.Equal(3, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_ManyTransactionsThatSkipTheApplicationLock_When_AllCommit_Should_ReserveExactlyTheStock()
    {
        // Arrange: 10 transações de 1 ingresso para um lote de 3, todas gravadas antes de qualquer confirmação.
        var stock = await GivenBatchWithStockAsync(quantity: 3);
        var connections = new List<NpgsqlConnection>();
        var transactions = new List<NpgsqlTransaction>();

        try
        {
            for (var index = 0; index < 10; index++)
            {
                var connection = await _fixture.OpenConnectionAsync();
                var transaction = await connection.BeginTransactionAsync();

                connections.Add(connection);
                transactions.Add(transaction);

                await InsertOrderWithItemAsync(connection, transaction, stock, 1);
            }

            // Act
            var commits = await Task.WhenAll(transactions.Select(TryCommitAsync));

            // Assert
            Assert.Equal(3, commits.Count(exception => exception is null));
            Assert.All(commits.Where(exception => exception is not null), exception => Assert.Equal(GUARD_CONSTRAINT, exception!.ConstraintName));
            Assert.Equal(3, await UsedAsync(stock));
        }
        finally
        {
            foreach (var transaction in transactions)
                await transaction.DisposeAsync();

            foreach (var connection in connections)
                await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Given_PaidOrderThatTakesTheWholeStock_When_AnotherReserves_Should_BeRefusedByTheDatabase()
    {
        // Arrange
        var stock = await GivenBatchWithStockAsync(quantity: 2);

        Assert.Null(await CommitRawOrderAsync(stock, 2, status: "PAID"));

        // Act
        var exception = await CommitRawOrderAsync(stock, 1);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(GUARD_CONSTRAINT, exception.ConstraintName);
    }

    [Fact]
    public async Task Given_ExpiredReservationOnTheWholeStock_When_AnotherReserves_Should_BeAcceptedByTheDatabase()
    {
        // Arrange
        var stock = await GivenBatchWithStockAsync(quantity: 2);

        Assert.Null(await CommitRawOrderAsync(stock, 2, expired: true));

        // Act
        var exception = await CommitRawOrderAsync(stock, 2);

        // Assert
        Assert.Null(exception);
        Assert.Equal(2, await UsedAsync(stock));
    }

    [Fact]
    public async Task Given_OrdersForDifferentBatches_When_ReservingTogether_Should_NotBlockOrRefuseEachOther()
    {
        // Arrange
        var first = await GivenBatchWithStockAsync(quantity: 3);
        var second = await GivenBatchWithStockAsync(quantity: 3);
        var customers = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GivenCustomerAsync()));

        // Act
        var results = await Task.WhenAll(
            customers.Take(3).Select(customerId => CreateOrderAsync(first, customerId, 1))
                .Concat(customers.Skip(3).Select(customerId => CreateOrderAsync(second, customerId, 1))));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(3, await UsedAsync(first));
        Assert.Equal(3, await UsedAsync(second));
    }

    private static async Task<PostgresException?> TryCommitAsync(NpgsqlTransaction transaction)
    {
        try
        {
            await transaction.CommitAsync();

            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }
}
