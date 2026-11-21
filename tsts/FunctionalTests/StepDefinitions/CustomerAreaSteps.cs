using Application.UseCases.Base.Authentication;
using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FunctionalTests.StepDefinitions;

/// <summary>Área do comprador (REQ13): pedidos e ingressos do cliente pela API, com o token dele.</summary>
[Binding]
public sealed class CustomerAreaSteps
{
    private const string LATEST_ORDER_SQL = """
        SELECT o.id_order FROM orders.orders o JOIN customers.customer c ON c.id_customer = o.id_customer
        WHERE c.full_name = @name ORDER BY o.created_at DESC LIMIT 1;
        """;

    private const string CUSTOMER_ID_SQL = "SELECT id_customer FROM customers.customer WHERE full_name = @name ORDER BY created_at DESC LIMIT 1;";

    // Pedido vira pago e ganha n ingressos no nome do titular, pelo mesmo efeito da confirmação do webhook (REQ8).
    private const string EMIT_TICKETS_SQL = """
        UPDATE orders.orders SET status = 'PAID', paid_at = now() WHERE id_order = @id_order;
        INSERT INTO orders.ticket (id_ticket, id_order_item, id_order, id_customer, id_event, code, holder_name, status, created_at, created_correlation_id)
        SELECT gen_random_uuid(), i.id_order_item, o.id_order, o.id_customer, o.id_event,
               upper(substr(md5(random()::text || g::text || i.id_order_item::text), 1, 12)), @holder, 'VALID', now(), gen_random_uuid()
        FROM orders.orders o
        JOIN orders.order_item i ON i.id_order = o.id_order
        CROSS JOIN LATERAL generate_series(1, @quantity) g
        WHERE o.id_order = @id_order;
        """;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private HttpResponseMessage? _response;
    private string _body = string.Empty;
    private string? _ticketCode;

    private sealed record Notification(string Code, string Message, string Type);
    private sealed record OrderItem(string Code, string Status);
    private sealed record OrdersPage(OrderItem[] Items, int Page, int PageSize, int TotalItems);
    private sealed record TicketEvent(string Slug, string Title, DateTime StartsAt);
    private sealed record TicketItem(string Code, string Status, string BatchName, string HolderName, string OrderCode, TicketEvent Event);
    private sealed record TicketsPage(TicketItem[] Items, int Page, int PageSize, int TotalItems);
    private sealed record TicketDetail(string Code, string Status, string BatchName, string HolderName, string OrderCode, TicketEvent Event, string AgeRating, DateTime IssuedAt);

    private static string Pt(string name) => $"{name} Souza";

    private async Task<(Guid Id, string Token)> CustomerAsync(string name)
    {
        var id = await NtixApiHooks.Database.ScalarAsync<Guid>(CUSTOMER_ID_SQL, new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = Pt(name) });
        var token = NtixApiHooks.Factory.Services.GetRequiredService<CustomerAccessTokenIssuer>().Issue(id, Guid.NewGuid(), DateTime.UtcNow);

        return (id, token);
    }

    private async Task GetAsync(string name, string route)
    {
        var (_, token) = await CustomerAsync(name);

        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        _response = await _client.SendAsync(request);
        _body = await _response.Content.ReadAsStringAsync();
    }

    private T Read<T>() => JsonSerializer.Deserialize<T>(_body, JSON_OPTIONS)!;

    [Given(@"^que os ingressos do pedido mais recente de (\w+) foram emitidos: (\d+) ingressos? ""([^""]*)""$")]
    public async Task GivenTicketsOfTheLatestOrderWereIssued(string name, int quantity, string batch)
    {
        var latestId = await NtixApiHooks.Database.ScalarAsync<Guid>(LATEST_ORDER_SQL, new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = Pt(name) });

        await NtixApiHooks.Database.ExecuteAsync(
            EMIT_TICKETS_SQL,
            new NpgsqlParameter("@id_order", NpgsqlDbType.Uuid) { Value = latestId },
            new NpgsqlParameter("@holder", NpgsqlDbType.Varchar) { Value = Pt(name) },
            new NpgsqlParameter("@quantity", NpgsqlDbType.Integer) { Value = quantity });
    }

    [When(@"^(\w+) lista os pedidos$")]
    public async Task WhenListsTheOrders(string name)
        => await GetAsync(name, "/api/v1/customers/me/orders");

    [When(@"^(\w+) lista os pedidos com status ""([^""]*)""$")]
    public async Task WhenListsTheOrdersWithStatus(string name, string status)
        => await GetAsync(name, $"/api/v1/customers/me/orders?status={status}");

    [When(@"^(\w+) lista os pedidos com pageSize (\d+)$")]
    public async Task WhenListsTheOrdersWithPageSize(string name, int pageSize)
        => await GetAsync(name, $"/api/v1/customers/me/orders?pageSize={pageSize}");

    [When(@"^(\w+) lista os ingressos próximos$")]
    public async Task WhenListsTheUpcomingTickets(string name)
        => await GetAsync(name, "/api/v1/customers/me/tickets?when=UPCOMING");

    [When(@"^(\w+) abre o primeiro ingresso dela$")]
    public async Task WhenOpensTheFirstTicket(string name)
    {
        await GetAsync(name, "/api/v1/customers/me/tickets");

        _ticketCode = Read<TicketsPage>().Items[0].Code;

        await GetAsync(name, $"/api/v1/customers/me/tickets/{_ticketCode}");
    }

    [When(@"^(\w+) abre o ingresso de (\w+)$")]
    public async Task WhenOpensAnotherCustomerTicket(string name, string other)
    {
        await CustomerAsync(other);
        var listing = await NtixApiHooks.Database.ScalarAsync<string>(
            "SELECT t.code FROM orders.ticket t JOIN customers.customer c ON c.id_customer = t.id_customer WHERE c.full_name = @name LIMIT 1;",
            new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = Pt(other) });

        await GetAsync(name, $"/api/v1/customers/me/tickets/{listing}");
    }

    [Then(@"^vê (\d+) pedidos?$")]
    public void ThenSeesOrders(int count)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(count, Read<OrdersPage>().TotalItems);
    }

    [Then(@"^o primeiro pedido está ""([^""]*)"" e o segundo ""([^""]*)""$")]
    public void ThenFirstAndSecondOrderStatus(string first, string second)
    {
        var items = Read<OrdersPage>().Items;

        Assert.Equal(first, items[0].Status);
        Assert.Equal(second, items[1].Status);
    }

    [Then(@"^vê (\d+) ingressos? do ""([^""]*)""$")]
    public void ThenSeesTickets(int count, string title)
    {
        var page = Read<TicketsPage>();

        Assert.Equal(count, page.TotalItems);
        Assert.All(page.Items, item => Assert.Equal(title, item.Event.Title));
    }

    [Then(@"^vê o evento ""([^""]*)"", o lote ""([^""]*)"", o titular ""([^""]*)"" e o status ""([^""]*)""$")]
    public void ThenSeesTicketDetail(string title, string batch, string holder, string status)
    {
        var detail = Read<TicketDetail>();

        Assert.Equal(title, detail.Event.Title);
        Assert.Equal(batch, detail.BatchName);
        Assert.Equal(holder, detail.HolderName);
        Assert.Equal(status, detail.Status);
        Assert.Equal(_ticketCode, detail.Code);
    }

    [Then(@"^vê só (\d+) pedido com status ""([^""]*)""$")]
    public void ThenSeesOnlyOrdersWithStatus(int count, string status)
    {
        var page = Read<OrdersPage>();

        Assert.Equal(count, page.TotalItems);
        Assert.All(page.Items, item => Assert.Equal(status, item.Status));
    }

    [Then(@"^recebe (\d+) com ""([^""]*)""$")]
    public void ThenReceivesStatusWithCode(int status, string code)
    {
        Assert.Equal((HttpStatusCode)status, _response!.StatusCode);
        Assert.Contains(Read<Notification[]>(), notification => notification.Code == code);
    }
}
