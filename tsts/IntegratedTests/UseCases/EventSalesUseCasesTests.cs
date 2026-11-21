using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetEventDailySales.Inputs;
using Application.UseCases.GetEventDailySales.Outputs;
using Application.UseCases.GetProducerEvent.Inputs;
using Application.UseCases.GetProducerEvents.Inputs;
using Application.UseCases.GetProducerEvents.Outputs;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using UnitTests.Support;

namespace IntegratedTests.UseCases;

/// <summary>
/// Vendas do evento (REQ14) contra o PostgreSQL real: gravação na efetivação, contadores por lote e por evento, concorrência,
/// webhook repetido, reconciliação, partições, vendas por dia e a situação ENDED no filtro.
/// </summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class EventSalesUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string PROVIDER_REFERENCE_SQL = "SELECT p.provider_reference FROM payments.payment p JOIN orders.orders o ON o.id_order = p.id_order WHERE o.code = @code;";
    private const string SALE_ENTRIES_SQL = "SELECT COALESCE(SUM(quantity), 0)::int FROM orders.sale_entry WHERE id_event = @id;";
    private const string SALE_ENTRY_PARTITION_SQL = "SELECT DISTINCT tableoid::regclass::text FROM orders.sale_entry WHERE id_event = @id;";
    private const string EVENT_COUNTER_SQL = "SELECT sold_quantity FROM orders.event_sales WHERE id_event = @id;";
    private const string PAID_ITEMS_SQL = """
        SELECT COALESCE(SUM(item.quantity), 0)::int
        FROM orders.order_item item JOIN orders.orders o ON o.id_order = item.id_order
        WHERE o.id_event = @id AND o.status = 'PAID';
        """;
    private const string SET_ORDER_PAID_SQL = "UPDATE orders.orders SET status = 'PAID', paid_at = now() WHERE code = @code;";
    private const string SET_EVENT_ENDED_SQL = "UPDATE events.event SET starts_at = now() - interval '2 days', ends_at = now() - interval '1 minute' WHERE id_event = @id;";
    private const string ORDER_ITEM_SQL = "SELECT item.id_order_item FROM orders.order_item item JOIN orders.orders o ON o.id_order = item.id_order WHERE o.code = @code;";

    private readonly PostgreSqlFixture _fixture;

    public EventSalesUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record SalesEvent(string ProducerSlug, Guid ProducerId, Guid EventId, string EventSlug, Guid BatchId);

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

    private async Task<MethodResult> WebhookAsync(byte[] body)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<ConfirmPaymentFromWebhookUseCaseInput>>().ExecuteAsync(
            ConfirmPaymentFromWebhookUseCaseInput.Create(body, WebhookTestData.Sign(body), WebhookTestData.WEBHOOK_SECRET),
            CustomerTestData.NewAuditableInfo());
    }

    /// <summary>Produtora com Pix configurado e um evento publicado com um lote de 100 ingressos a R$ 120,00.</summary>
    private async Task<SalesEvent> GivenEventOnSaleAsync()
    {
        var (producerId, slug) = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integration = await ExecuteAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(CreateProducerIntegrationUseCaseInput.Create(
            slug, "ABACATEPAY", $"Conta {Guid.NewGuid():N}"[..20], new Dictionary<string, string?> { ["API_KEY"] = $"abc_dev_{Guid.NewGuid():N}", ["WEBHOOK_SECRET"] = WebhookTestData.WEBHOOK_SECRET }));

        Assert.True(integration.IsSuccess);

        Assert.True((await ExecuteAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(
            ReplacePaymentRouteUseCaseInput.Create(slug, "PIX", [integration.Output!.Id.ToString()]))).IsSuccess);

        var startsAt = DateTime.UtcNow.Date.AddDays(60).AddHours(21);
        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            slug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Local", "Rua A, 1", "Recife", "PE", "FREE", null))).Output!;
        var batch = (await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            slug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, 12000, 100, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")))).Output!;

        await _fixture.ExecuteAsync(PUBLISH_SQL, Id(created.Id));

        return new SalesEvent(slug, producerId, created.Id, created.Slug, batch.Id);
    }

    /// <summary>Pedido do comprador novo com o Pix gerado e já pago no fornecedor falso; devolve o código e o envelope do webhook.</summary>
    private async Task<(string Code, byte[] Envelope)> GivenPaidPixAsync(SalesEvent sales, int quantity)
    {
        var email = CustomerTestData.NewEmail();

        using (var scope = _fixture.Services.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>()
                .ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), CustomerTestData.PASSWORD, "81999990000"), CustomerTestData.NewAuditableInfo())).IsSuccess);
        }

        var customerId = (await _fixture.FindCustomerByEmailAsync(email))!.Id;
        var order = (await ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(
            CreateOrderUseCaseInput.Create(customerId.ToString(), sales.EventSlug, "HUB", [(sales.BatchId.ToString(), quantity)]))).Output!;

        Assert.True((await ExecuteAsync<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput>(CreateOrderPaymentUseCaseInput.Create(order.Code, customerId.ToString(), "PIX"))).IsSuccess);

        var reference = await _fixture.ScalarAsync<string>(PROVIDER_REFERENCE_SQL, Text("@code", order.Code));

        _fixture.PixClient.SetStatus(reference, "PAID");

        return (order.Code, WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", reference));
    }

    private async Task<ProducerEventOutput> GetEventAsync(SalesEvent sales)
        => (await ExecuteAsync<GetProducerEventUseCaseInput, ProducerEventOutput>(GetProducerEventUseCaseInput.Create(sales.ProducerSlug, sales.EventId.ToString()))).Output!;

    [Fact]
    public async Task Given_PaidOrder_When_ConsultingTheEvent_Should_ShowCapacitySoldGrossAndOrdersFromTheCounters()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        var (_, envelope) = await GivenPaidPixAsync(sales, quantity: 3);

        // Act
        Assert.True((await WebhookAsync(envelope)).IsSuccess);
        var summary = (await GetEventAsync(sales)).SalesSummary;

        // Assert (RN5, RN6, RN7, RN9)
        Assert.Equal(new ProducerEventSalesSummaryOutput(Capacity: 100, SoldQuantity: 3, GrossCents: 36000, PaidOrders: 1), summary);
        Assert.Equal(3, await _fixture.ScalarAsync<int>(SALE_ENTRIES_SQL, Id(sales.EventId)));
        Assert.Equal($"orders.sale_entry_{DateTime.UtcNow:yyyy_MM}", Assert.Single(await PartitionsAsync(sales.EventId)));
    }

    [Fact]
    public async Task Given_PendingOrder_When_ConsultingTheEvent_Should_NotCountTheReservation()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        await GivenPaidPixAsync(sales, quantity: 2);

        // Act: o Pix foi gerado, mas o webhook ainda não chegou.
        var summary = (await GetEventAsync(sales)).SalesSummary;

        // Assert (RN6)
        Assert.Equal(0, summary.SoldQuantity);
        Assert.Equal(0, summary.GrossCents);
    }

    [Fact]
    public async Task Given_ConfirmedOrder_When_SameWebhookArrivesAgain_Should_NotCountTheSaleTwice()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        var (_, envelope) = await GivenPaidPixAsync(sales, quantity: 2);

        await WebhookAsync(envelope);

        // Act
        await WebhookAsync(envelope);

        // Assert
        Assert.Equal(2, (await GetEventAsync(sales)).SalesSummary.SoldQuantity);
        Assert.Equal(2, await _fixture.ScalarAsync<int>(SALE_ENTRIES_SQL, Id(sales.EventId)));
    }

    [Fact]
    public async Task Given_TwoPaymentsOfTheSameBatchAtOnce_When_Confirming_Should_SumBothAndReconcileWithTheOrders()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        var first = await GivenPaidPixAsync(sales, quantity: 2);
        var second = await GivenPaidPixAsync(sales, quantity: 5);

        // Act
        var results = await Task.WhenAll(WebhookAsync(first.Envelope), WebhookAsync(second.Envelope));

        // Assert: o upsert atômico não perde incremento, e contadores, fatos e pedidos batem.
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(7, (await GetEventAsync(sales)).SalesSummary.SoldQuantity);
        Assert.Equal(7, await _fixture.ScalarAsync<int>(EVENT_COUNTER_SQL, Id(sales.EventId)));
        Assert.Equal(7, await _fixture.ScalarAsync<int>(SALE_ENTRIES_SQL, Id(sales.EventId)));
        Assert.Equal(7, await _fixture.ScalarAsync<int>(PAID_ITEMS_SQL, Id(sales.EventId)));

        var batch = Assert.Single((await ExecuteAsync<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>(
            GetTicketBatchesUseCaseInput.Create(sales.ProducerSlug, sales.EventId.ToString()))).Output!.Batches);

        Assert.Equal(84000, batch.GrossCents);
        Assert.Equal(2, batch.PaidOrders);
    }

    [Fact]
    public async Task Given_OrderPaidBeforeTheSalesTables_When_ReconcilingTwice_Should_BackfillOnceAndKeepTheCounters()
    {
        // Arrange: pedido pago direto no banco, como os pagos antes do REQ14.
        var sales = await GivenEventOnSaleAsync();
        var (code, _) = await GivenPaidPixAsync(sales, quantity: 4);
        await _fixture.ExecuteAsync(SET_ORDER_PAID_SQL, Text("@code", code));

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "data", "orders", "009-reconcile-sales.sql"));

        // Act
        await _fixture.ExecuteAsync(script);
        await _fixture.ExecuteAsync(script);

        // Assert (RN17)
        var summary = (await GetEventAsync(sales)).SalesSummary;

        Assert.Equal(4, summary.SoldQuantity);
        Assert.Equal(48000, summary.GrossCents);
        Assert.Equal(1, summary.PaidOrders);
        Assert.Equal(4, await _fixture.ScalarAsync<int>(SALE_ENTRIES_SQL, Id(sales.EventId)));
    }

    [Fact]
    public async Task Given_SaleOutsideTheCreatedMonths_When_Registering_Should_FallIntoTheDefaultPartitionAndSayIt()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        var (code, _) = await GivenPaidPixAsync(sales, quantity: 1);
        var itemId = await _fixture.ScalarAsync<Guid>(ORDER_ITEM_SQL, Text("@code", code));
        var paidAt = new DateTime(2031, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<ISalesRepository>();

        await unitOfWork.BeginTransactionAsync();

        // Act
        var defaultRows = await repository.RegisterPaidOrderAsync([new SaleEntryModel(itemId, Guid.NewGuid(), sales.ProducerId, sales.EventId, sales.BatchId, 1, 120m, paidAt, Guid.NewGuid())]);

        await unitOfWork.RollbackAsync();

        // Assert (RN14)
        Assert.Equal(1, defaultRows);
    }

    [Fact]
    public async Task Given_SalesOnTwoDays_When_QueryingDailySales_Should_FillTheDayWithoutSalesWithZero()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        var (code, _) = await GivenPaidPixAsync(sales, quantity: 2);
        var itemId = await _fixture.ScalarAsync<Guid>(ORDER_ITEM_SQL, Text("@code", code));

        using (var scope = _fixture.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<ISalesRepository>();

            // 01/11 às 23h30 em Brasília é 02/11 em UTC: o dia do relatório é o de Brasília.
            await unitOfWork.BeginTransactionAsync();
            await repository.RegisterPaidOrderAsync([new SaleEntryModel(itemId, Guid.NewGuid(), sales.ProducerId, sales.EventId, sales.BatchId, 2, 120m, new DateTime(2026, 11, 2, 2, 30, 0, DateTimeKind.Utc), Guid.NewGuid())]);
            await unitOfWork.CommitAsync();
        }

        // Act
        var result = await ExecuteAsync<GetEventDailySalesUseCaseInput, GetEventDailySalesUseCaseOutput>(
            GetEventDailySalesUseCaseInput.Create(sales.ProducerSlug, sales.EventId.ToString(), "2026-11-01", "2026-11-03"));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new DailySalesOutput("2026-11-01", 2, 24000, 1), new DailySalesOutput("2026-11-02", 0, 0, 0), new DailySalesOutput("2026-11-03", 0, 0, 0)],
            result.Output!.Days);
    }

    [Fact]
    public async Task Given_OneMonthPeriod_When_PlanningTheDailyQuery_Should_ReadOnlyThatMonthPartition()
    {
        // Arrange
        var sql = """
            EXPLAIN (FORMAT TEXT)
            SELECT SUM(quantity) FROM orders.sale_entry
            WHERE id_event = '00000000-0000-0000-0000-000000000001' AND paid_at >= '2026-11-01T03:00:00Z' AND paid_at < '2026-12-01T03:00:00Z';
            """;

        // Act
        var plan = await PlanAsync(sql);

        // Assert (RN11): poda de partições pelo paid_at.
        Assert.Contains("sale_entry_2026_11", plan);
        Assert.Contains("sale_entry_2026_12", plan);
        Assert.DoesNotContain("sale_entry_2026_10", plan);
        Assert.DoesNotContain("sale_entry_2027_01", plan);
    }

    [Fact]
    public async Task Given_EventThatHasEnded_When_ListingByStatus_Should_ComeAsEndedOnlyInTheEndedFilter()
    {
        // Arrange
        var sales = await GivenEventOnSaleAsync();
        await _fixture.ExecuteAsync(SET_EVENT_ENDED_SQL, Id(sales.EventId));

        // Act
        var ended = await ListAsync(sales.ProducerSlug, "ENDED");
        var published = await ListAsync(sales.ProducerSlug, "PUBLISHED");

        // Assert (RN1, RN3)
        Assert.Equal("ENDED", Assert.Single(ended).Status);
        Assert.Empty(published);
        Assert.Equal("ENDED", (await GetEventAsync(sales)).Status);
    }

    private async Task<IReadOnlyList<ProducerEventOutput>> ListAsync(string producerSlug, string status)
        => (await ExecuteAsync<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput>(GetProducerEventsUseCaseInput.Create(producerSlug, status))).Output!.Events;

    private async Task<IReadOnlyList<string>> PartitionsAsync(Guid eventId)
    {
        await using var connection = await _fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(SALE_ENTRY_PARTITION_SQL, connection);

        command.Parameters.Add(Id(eventId));

        var partitions = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            partitions.Add(reader.GetString(0));

        return partitions;
    }

    private async Task<string> PlanAsync(string sql)
    {
        await using var connection = await _fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var lines = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));

        return string.Join('\n', lines);
    }
}
