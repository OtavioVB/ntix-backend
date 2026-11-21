using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.GetEventDailySales.Outputs;
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
using UnitTests.Support;
using WebApi.Controllers;

namespace FunctionalTests.StepDefinitions;

/// <summary>Vendas dos eventos (REQ14): pagamento confirmado pelo webhook assinado e as consultas do painel.</summary>
[Binding]
public sealed class EventSalesSteps
{
    private const string PASSWORD = "Senha@123";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @slug, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string PROVIDER_REFERENCE_SQL = "SELECT p.provider_reference FROM payments.payment p JOIN orders.orders o ON o.id_order = p.id_order WHERE o.code = @code;";
    private const string END_EVENT_SQL = "UPDATE events.event SET starts_at = now() - interval '2 days', doors_open_at = NULL, ends_at = now() - interval '1 minute' WHERE id_event = @id;";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);
    private static readonly DateTime EVENT_STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);
    private static readonly TimeZoneInfo BRASILIA = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private string _producerSlug = string.Empty;
    private string _producerToken = string.Empty;
    private Guid _eventId;
    private string _eventSlug = string.Empty;
    private string _eventTitle = string.Empty;
    private Guid _batchId;
    private byte[] _lastEnvelope = [];
    private HttpResponseMessage? _response;
    private string _body = string.Empty;

    private sealed record IdResponse(Guid Id);
    private sealed record OrderResponse(string Code);
    private sealed record NotificationResponse(string Code, string Message, string Type);

    #region Apoio

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BRASILIA)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    /// <summary>Comprador novo reserva os ingressos e gera o Pix; devolve o código do pedido.</summary>
    private async Task<string> ReserveAsync(int quantity)
    {
        var email = $"comprador.{Guid.NewGuid():N}@exemplo.com";

        await SendAsync(HttpMethod.Post, "/api/v1/customers", body: new { fullName = "Comprador Souza", email, document = NewCpf(), password = PASSWORD, phoneNumber = "81999990000" });

        Assert.True(_response!.IsSuccessStatusCode, _body);

        var customerId = await NtixApiHooks.Database.ScalarAsync<Guid>("SELECT id_customer FROM customers.customer WHERE email = @email;", new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = email });
        var token = NtixApiHooks.Factory.Services.GetRequiredService<CustomerAccessTokenIssuer>().Issue(customerId, Guid.NewGuid(), DateTime.UtcNow);

        await SendAsync(HttpMethod.Post, "/api/v1/orders", token, new { eventSlug = _eventSlug, salesChannel = "HUB", items = new[] { new { batchId = _batchId, quantity } } });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var code = Read<OrderResponse>().Code;

        await SendAsync(HttpMethod.Post, $"/api/v1/orders/{code}/payments", token, new { method = "PIX" });

        Assert.True(_response!.IsSuccessStatusCode, _body);

        return code;
    }

    /// <summary>O AbacatePay avisa o pagamento: envelope assinado, como no webhook real (REQ8).</summary>
    private async Task NotifyPaymentAsync(byte[] envelope)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment-webhooks/abacatepay") { Content = new ByteArrayContent(envelope) };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(PaymentWebhooksV1Controller.WEBHOOK_SIGNATURE_HEADER_KEY, WebhookTestData.Sign(envelope));
        request.Headers.Add(PaymentWebhooksV1Controller.WEBHOOK_SECRET_HEADER_KEY, WebhookTestData.WEBHOOK_SECRET);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task PayAsync(int quantity)
    {
        var code = await ReserveAsync(quantity);
        var reference = await NtixApiHooks.Database.ScalarAsync<string>(PROVIDER_REFERENCE_SQL, new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = code });

        NtixApiHooks.PixClient.SetStatus(reference, "PAID");

        _lastEnvelope = WebhookTestData.Envelope($"log_{Guid.NewGuid():N}", reference);

        await NotifyPaymentAsync(_lastEnvelope);
    }

    private async Task<ProducerEventOutput> EventFromListAsync(string title)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        return Assert.Single(Read<ProducerEventOutput[]>(), item => item.Title == title);
    }

    #endregion

    [Given(@"^que a produtora ""([^""]*)"" vende o evento ""([^""]*)"" com um lote de (\d+) ingressos a (\d+) centavos$")]
    public async Task GivenProducerSellingEvent(string slug, string title, int quantity, int priceCents)
    {
        var producerId = Guid.NewGuid();

        _producerSlug = slug;
        _eventTitle = title;

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = producerId },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        _producerToken = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(Guid.NewGuid(), producerId, slug, "ADMINISTRATOR", Guid.NewGuid(), DateTime.UtcNow);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/payment-integrations", _producerToken, new
        {
            providerCode = "ABACATEPAY",
            name = "Conta principal",
            credentials = new Dictionary<string, string> { ["API_KEY"] = $"abc_dev_{Guid.NewGuid():N}", ["WEBHOOK_SECRET"] = WebhookTestData.WEBHOOK_SECRET },
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var integrationId = Read<IdResponse>().Id;

        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{slug}/payment-routes/PIX", _producerToken, new { integrationIds = new[] { integrationId } });

        Assert.True(_response!.IsSuccessStatusCode, _body);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/events", _producerToken, new
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

        var created = Read<ProducerEventOutput>();

        _eventId = created.Id;
        _eventSlug = created.Slug;

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/events/{_eventId}/batches", _producerToken, new
        {
            name = "Pista",
            priceCents,
            quantity,
            salesStartsAt = Iso(DateTime.UtcNow.AddDays(-1)),
            salesEndsAt = Iso(EVENT_STARTS_AT),
            status = "ACTIVE",
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        _batchId = Read<IdResponse>().Id;

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/events/{_eventId}/publication", _producerToken);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
    }

    [Given(@"^que um comprador pagou (\d+) ingressos$")]
    [Given(@"^que outro comprador pagou (\d+) ingressos$")]
    public Task GivenABuyerPaid(int quantity)
        => PayAsync(quantity);

    [Given(@"^que um comprador reservou (\d+) ingressos e ainda não pagou$")]
    public Task GivenABuyerReserved(int quantity)
        => ReserveAsync(quantity);

    [Given("que o aviso desse pagamento chegou de novo")]
    public Task GivenTheNoticeArrivedAgain()
        => NotifyPaymentAsync(_lastEnvelope);

    [Given(@"^que o ""([^""]*)"" terminou há 1 minuto$")]
    public Task GivenTheEventEnded(string _)
        => NtixApiHooks.Database.ExecuteAsync(END_EVENT_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = _eventId });

    [When("consulto meus eventos")]
    public Task WhenIListMyEvents()
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events", _producerToken);

    [When("consulto os lotes do evento")]
    public Task WhenIListTheBatches()
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events/{_eventId}/batches", _producerToken);

    [When(@"^consulto as vendas por dia dos últimos (\d+) dias$")]
    public Task WhenIQueryTheLastDays(int days)
    {
        var today = DateOnly.ParseExact(Today(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = today.AddDays(-(days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events/{_eventId}/sales/daily?from={from}&to={Today()}", _producerToken);
    }

    [When("consulto as vendas por dia com o início depois do fim")]
    public Task WhenIQueryAnInvertedPeriod()
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events/{_eventId}/sales/daily?from=2026-11-03&to=2026-11-01", _producerToken);

    [When("filtro meus eventos encerrados")]
    public Task WhenIFilterEndedEvents()
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events?status=ENDED", _producerToken);

    [Then(@"^o ""([^""]*)"" mostra (\d+) de (\d+) ingressos vendidos$")]
    public async Task ThenTheEventShowsSold(string title, int sold, int capacity)
    {
        var summary = (await EventFromListAsync(title)).SalesSummary;

        Assert.Equal(sold, summary.SoldQuantity);
        Assert.Equal(capacity, summary.Capacity);
    }

    [Then(@"^o ""([^""]*)"" mostra (\d+) centavos em vendas brutas e (\d+) pedidos?$")]
    public async Task ThenTheEventShowsGross(string title, long grossCents, int orders)
    {
        var summary = (await EventFromListAsync(title)).SalesSummary;

        Assert.Equal(grossCents, summary.GrossCents);
        Assert.Equal(orders, summary.PaidOrders);
    }

    [Then(@"^o lote mostra (\d+) vendidos, (\d+) centavos em vendas brutas e (\d+) pedidos$")]
    public void ThenTheBatchShows(int sold, long grossCents, int orders)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        using var document = JsonDocument.Parse(_body);
        var batch = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(sold, batch.GetProperty("soldQuantity").GetInt32());
        Assert.Equal(grossCents, batch.GetProperty("grossCents").GetInt64());
        Assert.Equal(orders, batch.GetProperty("paidOrders").GetInt32());
    }

    [Then(@"^recebo (\d+) dias, e só hoje tem (\d+) ingressos vendidos$")]
    public void ThenIReceiveDays(int days, int sold)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var report = Read<GetEventDailySalesUseCaseOutput>();

        Assert.Equal(days, report.Days.Count);
        Assert.Equal(sold, Assert.Single(report.Days, day => day.SoldQuantity > 0).SoldQuantity);
        Assert.Equal(Today(), report.Days.Single(day => day.SoldQuantity > 0).Date);
    }

    [Then("sou avisada de que o período de vendas é inválido")]
    public void ThenThePeriodIsInvalid()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == "SALES_PERIOD_INVALID");
    }

    [Then(@"^vejo o ""([^""]*)"" encerrado$")]
    public async Task ThenISeeItEnded(string title)
        => Assert.Equal("ENDED", (await EventFromListAsync(title)).Status);

    [Then("ele não aparece entre os publicados")]
    public async Task ThenItIsNotAmongThePublished()
    {
        await SendAsync(HttpMethod.Get, $"/api/v1/producers/{_producerSlug}/events?status=PUBLISHED", _producerToken);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.DoesNotContain(Read<ProducerEventOutput[]>(), item => item.Title == _eventTitle);
    }
}
