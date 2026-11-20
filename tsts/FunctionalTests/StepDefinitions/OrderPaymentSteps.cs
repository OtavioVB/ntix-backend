using Application.UseCases.Base.Authentication;
using Domain.ValueObjects;
using FunctionalTests.Support;
using Infra.Http.Payments.Models;
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
public sealed class OrderPaymentSteps
{
    private const string PASSWORD = "Senha@123";
    private const string EVENT_TITLE = "Festival Horizonte";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string AGE_ORDER_SQL = """
        UPDATE orders.orders
        SET created_at = now() - make_interval(mins => @minutes), expires_at = now() - make_interval(mins => @minutes) + interval '15 minutes'
        WHERE code = @code;
        """;

    private const string MARK_ORDER_PAID_SQL = "UPDATE orders.orders SET status = 'PAID', paid_at = now() WHERE code = @code;";
    private const string PAYMENT_INTEGRATION_SQL = "SELECT id_producer_integration FROM payments.payment WHERE id_payment = @id;";
    private const string COUNT_PAYMENTS_SQL = "SELECT COUNT(*)::int FROM payments.payment WHERE id_order = (SELECT id_order FROM orders.orders WHERE code = @code);";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);
    private static readonly DateTime EVENT_STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, (Guid Id, string ApiKey)> _integrations = [];
    private readonly Dictionary<string, (Guid Id, string Token, string Cpf)> _customers = [];

    private string _producerSlug = string.Empty;
    private string _producerToken = string.Empty;
    private Guid _eventId;
    private string _eventSlug = string.Empty;
    private Guid _batchId;
    private string _orderCode = string.Empty;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;
    private string _firstCopyPaste = string.Empty;
    private PaymentResponse? _payment;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record IdResponse(Guid Id);
    private sealed record EventCreatedResponse(Guid Id, string Slug);
    private sealed record OrderResponse(string Code, DateTime ExpiresAt);
    private sealed record PixResponse(string CopyPasteCode, string QrCodeImage);
    private sealed record PaymentResponse(Guid Id, string Method, string Status, long AmountCents, DateTime ExpiresAt, PixResponse? Pix);
    private sealed record MethodResponse(string Method);
    private sealed record MethodsResponse(MethodResponse[] Methods);

    #region Apoio

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

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

    private async Task SendAsync(HttpMethod method, string route, string? token = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        _response = await _client.SendAsync(request);
        _body = await _response.Content.ReadAsStringAsync();
    }

    private T Read<T>()
        => JsonSerializer.Deserialize<T>(_body, JSON_OPTIONS)!;

    private async Task<(Guid Id, string Token, string Cpf)> CustomerAsync(string name)
    {
        if (_customers.TryGetValue(name, out var existing))
            return existing;

        var email = $"{name.ToLowerInvariant()}.{Guid.NewGuid():N}@exemplo.com";
        var cpf = NewCpf();

        await SendAsync(HttpMethod.Post, "/api/v1/customers", body: new { fullName = $"{name} Souza", email, document = cpf, password = PASSWORD, phoneNumber = "81999990000" });

        Assert.True(_response!.IsSuccessStatusCode, _body);

        var id = await NtixApiHooks.Database.ScalarAsync<Guid>("SELECT id_customer FROM customers.customer WHERE email = @email;", new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = email });
        var token = NtixApiHooks.Factory.Services.GetRequiredService<CustomerAccessTokenIssuer>().Issue(id, Guid.NewGuid(), DateTime.UtcNow);

        return _customers[name] = (id, token, cpf);
    }

    private Task PayAsync(string customerName)
        => PayOrderAsync(customerName, _orderCode);

    private async Task PayOrderAsync(string customerName, string orderCode)
    {
        var (_, token, _) = await CustomerAsync(customerName);

        await SendAsync(HttpMethod.Post, $"/api/v1/orders/{orderCode}/payments", token, new { method = "PIX" });
    }


    #endregion

    [Given(@"^o produtor ""([^""]*)"" com as integrações AbacatePay ""([^""]*)"" e ""([^""]*)"" na rota de PIX, nessa ordem$")]
    public async Task GivenProducerWithIntegrations(string slug, string first, string second)
    {
        var id = Guid.NewGuid();

        _producerSlug = slug;

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = $"Produtora {slug}" },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        _producerToken = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(Guid.NewGuid(), id, slug, "ADMINISTRATOR", Guid.NewGuid(), DateTime.UtcNow);

        foreach (var name in new[] { first, second })
        {
            var apiKey = $"abc_dev_{Guid.NewGuid():N}";

            await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/payment-integrations", _producerToken, new { providerCode = "ABACATEPAY", name, credentials = new Dictionary<string, string> { ["API_KEY"] = apiKey } });

            Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

            _integrations[name] = (Read<IdResponse>().Id, apiKey);
        }

        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{slug}/payment-routes/PIX", _producerToken, new { integrationIds = new[] { _integrations[first].Id, _integrations[second].Id } });

        Assert.True(_response!.IsSuccessStatusCode, _body);
    }

    [Given(@"^o pedido de (\w+) de (\d+) ingressos de (\d+) centavos criado agora$")]
    public async Task GivenOrderCreatedNow(string customerName, int quantity, int priceCents)
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{_producerSlug}/events", _producerToken, new
        {
            title = EVENT_TITLE,
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

        _eventId = created.Id;
        _eventSlug = created.Slug;

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{_producerSlug}/events/{_eventId}/batches", _producerToken, new
        {
            name = "Pista",
            priceCents,
            quantity = 50,
            salesStartsAt = Iso(DateTime.UtcNow.AddDays(-1)),
            salesEndsAt = Iso(EVENT_STARTS_AT),
            status = "ACTIVE",
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        _batchId = Read<IdResponse>().Id;

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{_producerSlug}/events/{_eventId}/publication", _producerToken);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var (_, token, _) = await CustomerAsync(customerName);

        await SendAsync(HttpMethod.Post, "/api/v1/orders", token, new { eventSlug = _eventSlug, salesChannel = "HUB", items = new[] { new { batchId = _batchId, quantity } } });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        _orderCode = Read<OrderResponse>().Code;
    }

    [Given(@"^o pedido criado há (\d+) minutos$")]
    public Task GivenOrderCreatedMinutesAgo(int minutes)
        => NtixApiHooks.Database.ExecuteAsync(
            AGE_ORDER_SQL,
            new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = _orderCode },
            new NpgsqlParameter("@minutes", NpgsqlDbType.Integer) { Value = minutes });

    [Given(@"^que a ""([^""]*)"" não responde em 5 segundos$")]
    public void GivenAccountDoesNotAnswer(string name)
        => NtixApiHooks.PixClient.FailWith(_integrations[name].ApiKey, PixChargeResultModel.Unavailable("timeout"));

    [Given(@"^que ""([^""]*)"" e ""([^""]*)"" respondem 500$")]
    public void GivenAccountsAnswer500(string first, string second)
    {
        NtixApiHooks.PixClient.FailWith(_integrations[first].ApiKey, PixChargeResultModel.ProviderError("http_5xx"));
        NtixApiHooks.PixClient.FailWith(_integrations[second].ApiKey, PixChargeResultModel.ProviderError("http_5xx"));
    }

    [Given(@"^que (\w+) já gerou o Pix do pedido$")]
    public async Task GivenPixAlreadyGenerated(string customerName)
    {
        await PayAsync(customerName);

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        _firstCopyPaste = Read<PaymentResponse>().Pix!.CopyPasteCode;
    }

    [Given("que a rota de PIX do produtor está vazia")]
    public async Task GivenEmptyRoute()
    {
        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{_producerSlug}/payment-routes/PIX", _producerToken, new { integrationIds = Array.Empty<Guid>() });

        Assert.True(_response!.IsSuccessStatusCode, _body);
    }

    [Given(@"^que o pedido de (\w+) já foi pago$")]
    public Task GivenOrderAlreadyPaid(string customerName)
        => NtixApiHooks.Database.ExecuteAsync(MARK_ORDER_PAID_SQL, new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = _orderCode });

    [When(@"^(\w+) consulta os métodos do pedido$")]
    public async Task WhenCustomerListsMethods(string customerName)
    {
        var (_, token, _) = await CustomerAsync(customerName);

        await SendAsync(HttpMethod.Get, $"/api/v1/orders/{_orderCode}/payment-methods", token);
    }

    [When(@"^(\w+) paga o pedido com PIX$")]
    public Task WhenCustomerPays(string customerName)
        => PayAsync(customerName);

    [When(@"^(\w+) paga o pedido com PIX de novo$")]
    public Task WhenCustomerPaysAgain(string customerName)
        => PayAsync(customerName);

    [When(@"^(\w+) paga o pedido de (\w+)$")]
    public Task WhenAnotherCustomerPaysTheOrder(string payer, string owner)
        => PayOrderAsync(payer, _orderCode);

    [Then("recebe só PIX")]
    public void ThenReceivesOnlyPix()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal("PIX", Assert.Single(Read<MethodsResponse>().Methods).Method);
    }

    [Then("recebe o QR Code e o Pix copia e cola")]
    public void ThenReceivesTheQrCodeAndCopyPaste()
    {
        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var payment = _payment = Read<PaymentResponse>();

        Assert.Equal("PENDING", payment.Status);
        Assert.False(string.IsNullOrWhiteSpace(payment.Pix!.CopyPasteCode));
        Assert.StartsWith("data:image/png;base64,", payment.Pix.QrCodeImage);
        Assert.Equal(39000, payment.AmountCents);
    }

    [Then("o Pix vale até 2 minutos antes do fim da reserva")]
    public async Task ThenThePixEndsTwoMinutesBeforeTheReservation()
    {
        var payment = _payment!;
        var (_, token, _) = await CustomerAsync("Marina");

        await SendAsync(HttpMethod.Get, $"/api/v1/orders/{_orderCode}", token);

        var order = Read<OrderResponse>();

        Assert.Equal(120, Math.Round((order.ExpiresAt - payment.ExpiresAt).TotalSeconds));
    }

    [Then(@"^o pagamento fica PENDING com a integração ""([^""]*)""$")]
    public async Task ThenThePaymentIsPendingWithTheIntegration(string name)
    {
        var payment = _payment!;

        Assert.Equal("PENDING", payment.Status);
        Assert.Equal(_integrations[name].Id, await NtixApiHooks.Database.ScalarAsync<Guid>(PAYMENT_INTEGRATION_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = payment.Id }));
    }

    [Then(@"^o AbacatePay recebeu (\d+) centavos com o CPF de (\w+)$")]
    public void ThenTheProviderReceived(int cents, string customerName)
    {
        var call = NtixApiHooks.PixClient.Calls.Last();

        Assert.Equal(cents, call.Request.AmountCents);
        Assert.Equal(_customers[customerName].Cpf, call.Request.CustomerTaxId);
        Assert.Equal(_integrations["Conta A"].ApiKey, call.ApiKey);
    }

    [Then("o pagamento é recusado com {string}")]
    public void ThenThePaymentIsRefused(string code)
    {
        var expected = code switch
        {
            "ORDER_NOT_FOUND" => HttpStatusCode.NotFound,
            "ORDER_ALREADY_PAID" or "ORDER_EXPIRED" or "ORDER_PAYMENT_WINDOW_TOO_SHORT" => HttpStatusCode.Conflict,
            "PAYMENT_PROVIDERS_UNAVAILABLE" => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.BadRequest,
        };

        Assert.Equal(expected, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }

    [Then("nenhum pagamento é gravado")]
    public async Task ThenNoPaymentIsSaved()
        => Assert.Equal(0, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PAYMENTS_SQL, new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = _orderCode }));

    [Then("recebe o mesmo Pix copia e cola")]
    public void ThenReceivesTheSamePix()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(_firstCopyPaste, Read<PaymentResponse>().Pix!.CopyPasteCode);
    }

    [Then("o AbacatePay foi chamado uma vez só")]
    public void ThenTheProviderWasCalledOnce()
        => Assert.Single(NtixApiHooks.PixClient.Calls);
}
