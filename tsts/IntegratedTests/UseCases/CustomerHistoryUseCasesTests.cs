using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetCustomerOrders.Inputs;
using Application.UseCases.GetCustomerTicket;
using Application.UseCases.GetCustomerTicket.Inputs;
using Application.UseCases.GetCustomerTickets.Inputs;
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
/// Área do comprador (REQ13) contra o PostgreSQL real: pedidos e ingressos por cliente, status efetivo no filtro, período dos ingressos,
/// paginação e isolamento entre compradores.
/// </summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class CustomerHistoryUseCasesTests
{
    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id;";
    private const string SET_EVENT_PAST_SQL = "UPDATE events.event SET starts_at = now() - interval '2 days', ends_at = now() - interval '1 day' WHERE id_event = @id;";
    private const string SET_ORDER_EXPIRED_SQL = "UPDATE orders.orders SET expires_at = now() - interval '1 minute' WHERE code = @code;";
    private const string SET_ORDER_PAID_SQL = "UPDATE orders.orders SET status = 'PAID', paid_at = now() WHERE code = @code;";
    private const string ORDER_ID_SQL = "SELECT id_order FROM orders.orders WHERE code = @code;";
    private const string ORDER_ITEM_SQL = "SELECT id_order_item FROM orders.order_item WHERE id_order = @id ORDER BY id_order_item LIMIT 1;";
    private const string ITEM_QUANTITY_SQL = "SELECT quantity FROM orders.order_item WHERE id_order_item = @id;";
    private const string TICKET_CODE_SQL = "SELECT t.code FROM orders.ticket t JOIN orders.orders o ON o.id_order = t.id_order WHERE o.code = @code;";

    private readonly PostgreSqlFixture _fixture;

    public CustomerHistoryUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record PublishedEvent(Guid Id, string Slug, Guid BatchId);

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

    private async Task<Guid> GivenCustomerAsync()
    {
        var email = CustomerTestData.NewEmail();

        using (var scope = _fixture.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>()
                .ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), CustomerTestData.PASSWORD, "81999990000"), CustomerTestData.NewAuditableInfo());

            Assert.True(created.IsSuccess);
        }

        return (await _fixture.FindCustomerByEmailAsync(email))!.Id;
    }

    /// <summary>Produtora nova: devolve o slug, que é o que os casos de uso de evento recebem.</summary>
    private async Task<string> GivenProducerSlugAsync()
    {
        var (_, slug) = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);

        return slug;
    }

    /// <summary>Evento publicado que começa em <paramref name="startsAt"/>, com um lote de 100 ingressos a R$ 120,00.</summary>
    private async Task<PublishedEvent> GivenPublishedEventAsync(string producerSlug, DateTime startsAt)
    {
        var created = (await ExecuteAsync<CreateEventUseCaseInput, ProducerEventOutput>(CreateEventUseCaseInput.Create(
            producerSlug, $"Evento {Guid.NewGuid():N}"[..30], null, "FESTIVALS", Iso(startsAt), Iso(startsAt.AddHours(6)), null,
            "Parque das Dunas", "Av. Beira-Mar, 1200", "Recife", "PE", "FREE", null))).Output!;

        var batch = (await ExecuteAsync<CreateTicketBatchUseCaseInput, TicketBatchOutput>(CreateTicketBatchUseCaseInput.Create(
            producerSlug, created.Id.ToString(), TicketBatchConversions.BuildDetails("Pista", null, 12000, 100, Iso(DateTime.UtcNow.AddDays(-1)), Iso(startsAt), "ACTIVE")))).Output!;

        await _fixture.ExecuteAsync(PUBLISH_SQL, Id(created.Id));

        return new PublishedEvent(created.Id, created.Slug, batch.Id);
    }

    private async Task<string> GivenOrderAsync(Guid customerId, PublishedEvent published, int quantity = 2)
    {
        var order = (await ExecuteAsync<CreateOrderUseCaseInput, OrderOutput>(
            CreateOrderUseCaseInput.Create(customerId.ToString(), published.Slug, "HUB", [(published.BatchId.ToString(), quantity)]))).Output!;

        return order.Code;
    }

    /// <summary>Marca o pedido como pago e emite um ingresso por unidade, pelo repositório de ingressos (REQ8).</summary>
    private async Task GivenPaidOrderWithTicketsAsync(string code, Guid customerId, Guid eventId, string holderName)
    {
        await _fixture.ExecuteAsync(SET_ORDER_PAID_SQL, Text("@code", code));

        var orderId = await _fixture.ScalarAsync<Guid>(ORDER_ID_SQL, Text("@code", code));
        var orderItemId = await _fixture.ScalarAsync<Guid>(ORDER_ITEM_SQL, Id(orderId));
        var quantity = await _fixture.ScalarAsync<int>(ITEM_QUANTITY_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = orderItemId });

        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();

        var models = Enumerable.Range(0, quantity).Select(_ => new TicketModel(
            Id: Guid.NewGuid(),
            OrderItemId: orderItemId,
            OrderId: orderId,
            CustomerId: customerId,
            EventId: eventId,
            Code: TicketCodeValueObject.Generate().Value,
            HolderName: holderName,
            Status: "VALID",
            CreatedAt: DateTime.UtcNow,
            CreatedCorrelationId: Guid.NewGuid())).ToArray();

        await unitOfWork.BeginTransactionAsync();
        await tickets.CreateManyAsync(models);
        await unitOfWork.CommitAsync();
    }

    [Fact]
    public async Task Given_OrdersOfTwoProducers_When_ListingMyOrders_Should_ReturnMostRecentFirstAndOnlyMine()
    {
        // Arrange
        var customer = await GivenCustomerAsync();
        var other = await GivenCustomerAsync();
        var first = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var second = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(70).AddHours(21));
        var older = await GivenOrderAsync(customer, first);
        await Task.Delay(20);
        var newer = await GivenOrderAsync(customer, second);
        await GivenOrderAsync(other, first);

        // Act
        var result = await ExecuteAsync<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>(GetCustomerOrdersUseCaseInput.Create(customer.ToString(), null, null, null));

        // Assert (RN1, RN3)
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Output!.TotalItems);
        Assert.Equal(new[] { newer, older }, result.Output.Items.Select(item => item.Code).ToArray());
        Assert.All(result.Output.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Producer.Slug)));
    }

    [Fact]
    public async Task Given_PendingOrderPastItsReservation_When_FilteringExpired_Should_ShowItAsExpired()
    {
        // Arrange (RN3): reserva vencida, mesmo sem a escrita que a grava, aparece como EXPIRED.
        var customer = await GivenCustomerAsync();
        var published = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var code = await GivenOrderAsync(customer, published);
        await _fixture.ExecuteAsync(SET_ORDER_EXPIRED_SQL, Text("@code", code));

        // Act
        var result = await ExecuteAsync<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>(GetCustomerOrdersUseCaseInput.Create(customer.ToString(), "EXPIRED", null, null));

        // Assert
        Assert.Single(result.Output!.Items, item => item.Code == code && item.Status == "EXPIRED");
    }

    [Fact]
    public async Task Given_PaidAndPendingOrders_When_FilteringPaid_Should_ReturnOnlyPaid()
    {
        // Arrange
        var customer = await GivenCustomerAsync();
        var published = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var paidCode = await GivenOrderAsync(customer, published);
        await GivenOrderAsync(customer, published);
        await _fixture.ExecuteAsync(SET_ORDER_PAID_SQL, Text("@code", paidCode));

        // Act
        var result = await ExecuteAsync<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>(GetCustomerOrdersUseCaseInput.Create(customer.ToString(), "PAID", null, null));

        // Assert
        Assert.Equal(new[] { paidCode }, result.Output!.Items.Select(item => item.Code).ToArray());
    }

    [Fact]
    public async Task Given_TwoOrdersAndPageSizeOne_When_ListingSecondPage_Should_ReturnTheOlderOrderAndTheTotal()
    {
        // Arrange
        var customer = await GivenCustomerAsync();
        var published = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var older = await GivenOrderAsync(customer, published);
        await Task.Delay(20);
        await GivenOrderAsync(customer, published);

        // Act
        var result = await ExecuteAsync<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>(GetCustomerOrdersUseCaseInput.Create(customer.ToString(), null, 2, 1));

        // Assert
        Assert.Equal(2, result.Output!.TotalItems);
        Assert.Equal(older, result.Output.Items.Single().Code);
    }

    [Fact]
    public async Task Given_TicketsForUpcomingAndPastEvents_When_ListingUpcomingAndPast_Should_SplitByEventEnd()
    {
        // Arrange (RN5)
        var customer = await GivenCustomerAsync();
        var upcomingEvent = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var pastEvent = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        await _fixture.ExecuteAsync(SET_EVENT_PAST_SQL, Id(pastEvent.Id));

        var upcomingOrder = await GivenOrderAsync(customer, upcomingEvent, quantity: 2);
        await GivenPaidOrderWithTicketsAsync(upcomingOrder, customer, upcomingEvent.Id, "Marina Souza");

        var pastOrder = await GivenOrderAsync(customer, pastEvent, quantity: 1);
        await GivenPaidOrderWithTicketsAsync(pastOrder, customer, pastEvent.Id, "Marina Souza");

        // Act
        var upcoming = await ExecuteAsync<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput>(GetCustomerTicketsUseCaseInput.Create(customer.ToString(), null, null, null));
        var past = await ExecuteAsync<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput>(GetCustomerTicketsUseCaseInput.Create(customer.ToString(), "PAST", null, null));

        // Assert
        Assert.Equal(2, upcoming.Output!.TotalItems);
        Assert.All(upcoming.Output.Items, item => Assert.Equal(upcomingOrder, item.OrderCode));
        Assert.Equal(1, past.Output!.TotalItems);
        Assert.Equal(pastOrder, past.Output.Items.Single().OrderCode);
    }

    [Fact]
    public async Task Given_TicketOfAnotherCustomer_When_OpeningByCode_Should_ReportNotFound()
    {
        // Arrange (RN1)
        var owner = await GivenCustomerAsync();
        var intruder = await GivenCustomerAsync();
        var published = await GivenPublishedEventAsync(await GivenProducerSlugAsync(), DateTime.UtcNow.Date.AddDays(60).AddHours(21));
        var code = await GivenOrderAsync(owner, published, quantity: 1);
        await GivenPaidOrderWithTicketsAsync(code, owner, published.Id, "Marina Souza");
        var ticketCode = await _fixture.ScalarAsync<string>(TICKET_CODE_SQL, Text("@code", code));

        // Act
        var ownerResult = await ExecuteAsync<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput>(GetCustomerTicketUseCaseInput.Create(ticketCode, owner.ToString()));
        var intruderResult = await ExecuteAsync<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput>(GetCustomerTicketUseCaseInput.Create(ticketCode, intruder.ToString()));

        // Assert
        Assert.True(ownerResult.IsSuccess);
        Assert.Equal("Marina Souza", ownerResult.Output!.HolderName);
        Assert.Contains(intruderResult.Notifications, notification => notification.Code == GetCustomerTicketUseCase.TICKET_NOT_FOUND_NOTIFICATION_CODE);
    }
}
