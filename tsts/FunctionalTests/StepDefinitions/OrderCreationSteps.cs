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
using System.Net.Http.Json;
using System.Text.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class OrderCreationSteps
{
    private const string PASSWORD = "Senha@123";
    private const string INITIAL_BATCH_PREFIX = "Lote inicial de ";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    // O pedido pago de outra pessoa que deixa só alguns ingressos: o mesmo efeito de vendas anteriores.
    private const string INSERT_PAID_ORDER_SQL = """
        INSERT INTO orders.orders (id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
                                   created_at, created_correlation_id, updated_at, updated_correlation_id)
        SELECT @id_order, @code, e.id_producer, e.id_event, @id_customer, 'HUB', @total, 'PAID', now() + interval '15 minutes', now(),
               now(), gen_random_uuid(), now(), gen_random_uuid()
        FROM events.ticket_batch b JOIN events.event e ON e.id_event = b.id_event WHERE b.id_ticket_batch = @id_ticket_batch;
        INSERT INTO orders.order_item (id_order_item, id_order, id_ticket_batch, batch_name, quantity, unit_price)
        VALUES (gen_random_uuid(), @id_order, @id_ticket_batch, 'Pista', @quantity, 120.00);
        """;

    private const string AGE_ORDER_SQL = """
        UPDATE orders.orders
        SET created_at = now() - make_interval(mins => @minutes), expires_at = now() - make_interval(mins => @minutes) + interval '15 minutes'
        WHERE code = @code;
        """;

    private const string COUNT_LINKS_SQL = "SELECT COUNT(*)::int FROM customers.producer_customer WHERE id_customer = @id_customer AND id_producer = @id_producer;";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);
    private static readonly DateTime EVENT_STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, (Guid Id, string Token)> _producers = [];
    private readonly Dictionary<string, (string ProducerSlug, Guid Id, string Slug)> _events = [];
    private readonly Dictionary<string, (string EventTitle, Guid Id)> _batches = [];
    private readonly Dictionary<string, (Guid Id, string Token)> _customers = [];
    private readonly Dictionary<string, string> _orderCodes = [];

    private HttpResponseMessage? _response;
    private string _body = string.Empty;
    private (HttpStatusCode Status, string Body)[] _concurrent = [];

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record IdResponse(Guid Id);
    private sealed record EventCreatedResponse(Guid Id, string Slug);
    private sealed record OrderItemResponse(Guid BatchId, string BatchName, int Quantity, long UnitPriceCents, long TotalCents);
    private sealed record OrderResponse(string Code, string Status, DateTime CreatedAt, DateTime ExpiresAt, long TotalCents, OrderItemResponse[] Items);
    private sealed record BatchResponse(Guid Id, string Name, int AvailableQuantity);

    #region Apoio

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // CPF válido e único por cliente: o limite de tentativas e a unicidade vivem durante toda a execução.
    private static string NewCpf()
    {
        var digits = string.Concat(Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10)));

        static int CheckDigit(string value)
        {
            var sum = value.Select((digit, index) => (digit - '0') * (value.Length + 1 - index)).Sum();
            var rest = sum % 11;

            return rest < 2 ? 0 : 11 - rest;
        }

        digits += CheckDigit(digits);
        digits += CheckDigit(digits);

        return digits;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string route, string? token, object? body)
    {
        var request = new HttpRequestMessage(method, route);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        return request;
    }

    private async Task SendAsync(HttpMethod method, string route, string? token = null, object? body = null)
    {
        _response = await _client.SendAsync(BuildRequest(method, route, token, body));
        _body = await _response.Content.ReadAsStringAsync();
    }

    private T Read<T>()
        => JsonSerializer.Deserialize<T>(_body, JSON_OPTIONS)!;

    private async Task<(Guid Id, string Token)> ProducerAsync(string slug)
    {
        if (_producers.TryGetValue(slug, out var existing))
            return existing;

        var id = Guid.NewGuid();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = $"Produtora {slug}" },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        var token = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(Guid.NewGuid(), id, slug, "ADMINISTRATOR", Guid.NewGuid(), DateTime.UtcNow);

        return _producers[slug] = (id, token);
    }

    private async Task<(Guid Id, string Token)> CustomerAsync(string name)
    {
        if (_customers.TryGetValue(name, out var existing))
            return existing;

        var email = $"{name.ToLowerInvariant()}.{Guid.NewGuid():N}@exemplo.com";

        await SendAsync(HttpMethod.Post, "/api/v1/customers", body: new { fullName = $"{name} Souza", email, document = NewCpf(), password = PASSWORD });

        Assert.True(_response!.IsSuccessStatusCode, _body);

        var id = await NtixApiHooks.Database.ScalarAsync<Guid>("SELECT id_customer FROM customers.customer WHERE email = @email;", new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = email });
        var token = NtixApiHooks.Factory.Services.GetRequiredService<CustomerAccessTokenIssuer>().Issue(id, Guid.NewGuid(), DateTime.UtcNow);

        return _customers[name] = (id, token);
    }

    private async Task<Guid> PublishEventAsync(string title, string producerSlug)
    {
        var (_, token) = await ProducerAsync(producerSlug);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/events", token, new
        {
            title,
            category = "FESTIVALS",
            startsAt = Iso(EVENT_STARTS_AT),
            endsAt = Iso(EVENT_STARTS_AT.AddHours(9)),
            venueName = "Parque das Dunas",
            address = "Av. Beira-Mar, 1200",
            city = "Recife",
            state = "PE",
            ageRating = "AGE_16",
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var created = Read<EventCreatedResponse>();

        _events[title] = (producerSlug, created.Id, created.Slug);

        return created.Id;
    }

    private async Task PublishAsync(string title)
    {
        var (producerSlug, eventId, _) = _events[title];
        var (_, token) = await ProducerAsync(producerSlug);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/payment-integrations", token, new
        {
            providerCode = "ABACATEPAY",
            name = $"AbacatePay {Guid.NewGuid():N}"[..30],
            credentials = new Dictionary<string, string> { ["API_KEY"] = "abc_dev_chave-de-teste-1234" },
        });

        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{producerSlug}/payment-routes/PIX", token, new { integrationIds = new[] { Read<IdResponse>().Id } });
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/events/{eventId}/publication", token);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
    }

    private async Task CreateBatchAsync(string eventTitle, string name, long priceCents, int quantity, DateTime salesStartsAt)
    {
        var (producerSlug, eventId, _) = _events[eventTitle];
        var (_, token) = await ProducerAsync(producerSlug);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/events/{eventId}/batches", token, BatchBody(name, priceCents, quantity, salesStartsAt));

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        _batches[name] = (eventTitle, Read<IdResponse>().Id);
    }

    private static object BatchBody(string name, long priceCents, int quantity, DateTime salesStartsAt)
        => new
        {
            name,
            priceCents,
            quantity,
            salesStartsAt = Iso(salesStartsAt),
            salesEndsAt = Iso(EVENT_STARTS_AT),
            status = "ACTIVE",
        };

    private static object OrderBody(string eventSlug, params (Guid BatchId, int Quantity)[] items)
        => new
        {
            eventSlug,
            salesChannel = "HUB",
            items = items.Select(item => new { batchId = item.BatchId, quantity = item.Quantity }).ToArray(),
        };

    private async Task<HttpStatusCode> PlaceOrderAsync(string customerName, string batchName, int quantity, string? eventTitle = null)
    {
        var (_, token) = await CustomerAsync(customerName);
        var (title, batchId) = _batches[batchName];

        await SendAsync(HttpMethod.Post, "/api/v1/orders", token, OrderBody(_events[eventTitle ?? title].Slug, (batchId, quantity)));

        if (_response!.StatusCode == HttpStatusCode.Created)
            _orderCodes[customerName] = Read<OrderResponse>().Code;

        return _response.StatusCode;
    }

    private async Task<int> AvailableAsync(string batchName)
    {
        var (title, batchId) = _batches[batchName];
        var (producerSlug, eventId, _) = _events[title];
        var (_, token) = await ProducerAsync(producerSlug);

        await SendAsync(HttpMethod.Get, $"/api/v1/producers/{producerSlug}/events/{eventId}/batches", token);

        return Read<BatchResponse[]>().Single(batch => batch.Id == batchId).AvailableQuantity;
    }

    private void AssertRefused(string code)
    {
        var expected = code switch
        {
            "BATCH_INSUFFICIENT_AVAILABILITY" => HttpStatusCode.Conflict,
            "EVENT_NOT_FOUND" or "ORDER_NOT_FOUND" => HttpStatusCode.NotFound,
            _ => HttpStatusCode.BadRequest,
        };

        Assert.Equal(expected, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }

    #endregion

    [Given("o evento {string} publicado pela produtora {string}")]
    public async Task GivenPublishedEvent(string title, string producerSlug)
    {
        await PublishEventAsync(title, producerSlug);

        // O evento só é publicado com ao menos um lote: o lote inicial só existe para isso e não entra nos cenários.
        await CreateBatchAsync(title, $"{INITIAL_BATCH_PREFIX}{title}", 100000, 1, DateTime.UtcNow.AddDays(-1));
        await PublishAsync(title);
    }

    [Given("o lote {string} de {int} centavos com {int} ingressos em venda no evento {string}")]
    public Task GivenBatchOnSale(string name, int priceCents, int quantity, string eventTitle)
        => CreateBatchAsync(eventTitle, name, priceCents, quantity, DateTime.UtcNow.AddDays(-1));

    [Given("o lote {string} com venda a partir de amanhã no evento {string}")]
    public Task GivenBatchOpeningTomorrow(string name, string eventTitle)
        => CreateBatchAsync(eventTitle, name, 15000, 10, DateTime.UtcNow.AddDays(1));

    [Given(@"^a cliente (\w+) autenticada$")]
    public Task GivenCustomerAuthenticated(string name)
        => CustomerAsync(name);

    [Given(@"^que (?:restam|resta) (\d+) ingressos? ""([^""]*)""$")]
    public async Task GivenTicketsLeft(int left, string batchName)
    {
        var (_, batchId) = _batches[batchName];
        var (customerId, _) = await CustomerAsync("Anterior");
        var quantity = await AvailableAsync(batchName) - left;

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PAID_ORDER_SQL,
            new NpgsqlParameter("@id_order", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() },
            new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = OrderCodeValueObject.Generate().Value },
            new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId },
            new NpgsqlParameter("@total", NpgsqlDbType.Numeric) { Value = quantity * 120.00m },
            new NpgsqlParameter("@id_ticket_batch", NpgsqlDbType.Uuid) { Value = batchId },
            new NpgsqlParameter("@quantity", NpgsqlDbType.Integer) { Value = quantity });
    }

    [Given(@"^o pedido de (\w+) com (\d+) ingressos? ""([^""]*)""$")]
    public async Task GivenOrderOf(string customerName, int quantity, string batchName)
        => Assert.Equal(HttpStatusCode.Created, await PlaceOrderAsync(customerName, batchName, quantity));

    [Given(@"^o pedido de (\w+) com (\d+) ingressos? ""([^""]*)"" criado há (\d+) minutos$")]
    public async Task GivenOldOrderOf(string customerName, int quantity, string batchName, int minutes)
    {
        Assert.Equal(HttpStatusCode.Created, await PlaceOrderAsync(customerName, batchName, quantity));

        await NtixApiHooks.Database.ExecuteAsync(
            AGE_ORDER_SQL,
            new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = _orderCodes[customerName] },
            new NpgsqlParameter("@minutes", NpgsqlDbType.Integer) { Value = minutes });
    }

    [When(@"^(\w+) pede (\d+) ingressos? ""([^""]*)""$")]
    public Task WhenCustomerOrders(string customerName, int quantity, string batchName)
        => PlaceOrderAsync(customerName, batchName, quantity);

    [When(@"^(\w+) pede um lote do evento ""([^""]*)"" no pedido de ""([^""]*)""$")]
    public async Task WhenCustomerOrdersBatchOfAnotherEvent(string customerName, string batchEventTitle, string orderEventTitle)
    {
        var (_, token) = await CustomerAsync(customerName);
        var batchId = _batches.Single(pair => pair.Value.EventTitle == batchEventTitle && !pair.Key.StartsWith(INITIAL_BATCH_PREFIX, StringComparison.Ordinal)).Value.Id;

        await SendAsync(HttpMethod.Post, "/api/v1/orders", token, OrderBody(_events[orderEventTitle].Slug, (batchId, 1)));
    }

    [When(@"^(\w+) e (\w+) pedem (\d+) ingressos? ""([^""]*)"" ao mesmo tempo$")]
    public async Task WhenTwoCustomersOrderAtTheSameTime(string first, string second, int quantity, string batchName)
    {
        var (title, batchId) = _batches[batchName];
        var body = OrderBody(_events[title].Slug, (batchId, quantity));
        var tokens = new[] { (await CustomerAsync(first)).Token, (await CustomerAsync(second)).Token };

        _concurrent = await Task.WhenAll(tokens.Select(async token =>
        {
            var response = await _client.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/orders", token, body));

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }));
    }

    [When("o produtor muda o preço do lote {string} para {int} centavos")]
    public async Task WhenProducerChangesThePrice(string batchName, int priceCents)
    {
        var (title, batchId) = _batches[batchName];
        var (producerSlug, eventId, _) = _events[title];
        var (_, token) = await ProducerAsync(producerSlug);

        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{producerSlug}/events/{eventId}/batches/{batchId}", token, BatchBody(batchName, priceCents, 10, DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
    }

    [When(@"^(\w+) consulta o pedido$")]
    public async Task WhenCustomerReadsTheOrder(string customerName)
    {
        var (_, token) = await CustomerAsync(customerName);

        await SendAsync(HttpMethod.Get, $"/api/v1/orders/{_orderCodes[customerName]}", token);
    }

    [When(@"^(\w+) consulta o código do pedido de (\w+)$")]
    public async Task WhenCustomerReadsTheOrderOfAnother(string customerName, string ownerName)
    {
        var (_, token) = await CustomerAsync(customerName);

        await SendAsync(HttpMethod.Get, $"/api/v1/orders/{_orderCodes[ownerName]}", token);
    }

    [When(@"^(\w+) faz o primeiro pedido com o produtor ""([^""]*)""$")]
    public async Task WhenCustomerPlacesTheFirstOrderWithTheProducer(string customerName, string producerSlug)
    {
        var batchName = _batches.First(batch => _events[batch.Value.EventTitle].ProducerSlug == producerSlug && batch.Key == "Pista").Key;

        Assert.Equal(HttpStatusCode.Created, await PlaceOrderAsync(customerName, batchName, 1));
    }

    [When("alguém tenta pedir sem entrar")]
    public async Task WhenAnonymousTriesToOrder()
    {
        var (title, batchId) = _batches["Pista"];

        await SendAsync(HttpMethod.Post, "/api/v1/orders", body: OrderBody(_events[title].Slug, (batchId, 1)));
    }

    [When("o usuário da produtora tenta pedir")]
    public async Task WhenProducerUserTriesToOrder()
    {
        var (title, batchId) = _batches["Pista"];
        var (_, token) = await ProducerAsync("mare-cultural");

        await SendAsync(HttpMethod.Post, "/api/v1/orders", token, OrderBody(_events[title].Slug, (batchId, 1)));
    }

    [Then("recebe o pedido aguardando pagamento de {int} centavos")]
    public void ThenReceivesThePendingOrder(int totalCents)
    {
        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
        Assert.NotNull(_response.Headers.Location);

        var order = Read<OrderResponse>();

        Assert.Equal("PENDING_PAYMENT", order.Status);
        Assert.Equal(totalCents, order.TotalCents);
        Assert.EndsWith(order.Code, _response.Headers.Location!.ToString());
    }

    [Then("o pedido expira em {int} minutos")]
    public void ThenTheOrderExpiresIn(int minutes)
    {
        var order = Read<OrderResponse>();

        Assert.Equal(minutes, Math.Round((order.ExpiresAt - order.CreatedAt).TotalMinutes));
    }

    [Then(@"^restam (\d+) ingressos? ""([^""]*)""$")]
    public async Task ThenTicketsLeft(int left, string batchName)
        => Assert.Equal(left, await AvailableAsync(batchName));

    [Then("o pedido de {word} continua com {int} centavos")]
    public async Task ThenTheOrderKeepsTheTotal(string customerName, int totalCents)
    {
        await WhenCustomerReadsTheOrder(customerName);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(totalCents, Read<OrderResponse>().TotalCents);
    }

    [Then("o pedido é recusado com {string}")]
    public void ThenTheOrderIsRefused(string code)
        => AssertRefused(code);

    [Then("o pedido está expirado")]
    public void ThenTheOrderIsExpired()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal("EXPIRED", Read<OrderResponse>().Status);
    }

    [Then("só um dos dois recebe o pedido")]
    public void ThenOnlyOneOfThemGetsTheOrder()
        => Assert.Single(_concurrent, answer => answer.Status == HttpStatusCode.Created);

    [Then("o outro é recusado com {string}")]
    public void ThenTheOtherIsRefused(string code)
    {
        var refused = Assert.Single(_concurrent, answer => answer.Status != HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Contains(JsonSerializer.Deserialize<NotificationResponse[]>(refused.Body, JSON_OPTIONS)!, notification => notification.Code == code);
    }

    [Then("a consulta não encontra o pedido")]
    public void ThenTheOrderIsNotFound()
        => AssertRefused("ORDER_NOT_FOUND");

    [Then(@"^(\w+) passa a ser cliente de ""([^""]*)""$")]
    public async Task ThenCustomerIsLinked(string customerName, string producerSlug)
    {
        var (customerId, _) = await CustomerAsync(customerName);
        var (producerId, _) = await ProducerAsync(producerSlug);

        Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(
            COUNT_LINKS_SQL,
            new NpgsqlParameter("@id_customer", NpgsqlDbType.Uuid) { Value = customerId },
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = producerId }));
    }

    [Then("o pedido é recusado por falta de login")]
    public void ThenTheOrderIsRefusedForLackOfLogin()
        => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [Then("o pedido é recusado por não ser de cliente")]
    public void ThenTheOrderIsRefusedForNotBeingACustomer()
        => Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);
}
