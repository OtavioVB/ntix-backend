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
public sealed class TicketBatchesAndPublicationSteps
{
    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @slug, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string COUNT_OUTBOX_SQL = "SELECT COUNT(*)::int FROM outbox.outbox_event WHERE aggregate_id = @id AND event_type = 'EventPublished';";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, Guid> _batchesByName = [];

    private string _slug = null!;
    private string _token = null!;
    private Guid _eventId;
    private Guid _foreignEventId;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record IdResponse(Guid Id);
    private sealed record BatchResponse(Guid Id, string Name, int AvailableQuantity, string SaleState);
    private sealed record EventResponse(Guid Id, string Status);

    #region Apoio

    private static readonly DateTime EVENT_STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static async Task<(Guid Id, string Token)> RegisterProducerAsync(string slug)
    {
        var id = Guid.NewGuid();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        // O mesmo emissor do login (REQ5), sem passar pela senha.
        var token = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(
            producerUserId: Guid.NewGuid(),
            producerId: id,
            producerSlug: slug,
            userType: "ADMINISTRATOR",
            sessionId: Guid.NewGuid(),
            now: DateTime.UtcNow);

        return (id, token);
    }

    private async Task SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        _response = await _client.SendAsync(request);
        _body = await _response.Content.ReadAsStringAsync();
    }

    private T Read<T>()
        => JsonSerializer.Deserialize<T>(_body, JSON_OPTIONS)!;

    private async Task<Guid> CreateDraftEventAsync(string slug, string token, string title)
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/events", token, new
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

        return Read<IdResponse>().Id;
    }

    private static object BatchBody(string name, long priceCents, int quantity, DateTime? salesEndsAt = null, string status = "ACTIVE")
        => new
        {
            name,
            priceCents,
            quantity,
            salesStartsAt = Iso(DateTime.UtcNow.AddDays(-1)),
            salesEndsAt = Iso(salesEndsAt ?? EVENT_STARTS_AT),
            status,
        };

    private string BatchesRoute(string slug, Guid eventId)
        => $"/api/v1/producers/{slug}/events/{eventId}/batches";

    private async Task CreateBatchAsync(string name, long priceCents, int quantity, DateTime? salesEndsAt = null)
    {
        await SendAsync(HttpMethod.Post, BatchesRoute(_slug, _eventId), _token, BatchBody(name, priceCents, quantity, salesEndsAt));

        if (_response!.StatusCode == HttpStatusCode.Created)
            _batchesByName[name] = Read<BatchResponse>().Id;
    }

    #endregion

    [Given("que sou a produtora {string} montando o evento {string} em rascunho")]
    public async Task GivenIAmBuildingTheDraftEvent(string slug, string title)
    {
        _slug = slug;
        (_, _token) = await RegisterProducerAsync(slug);
        _eventId = await CreateDraftEventAsync(slug, _token, title);
    }

    [Given("que o evento já tem o lote {string}")]
    public async Task GivenTheEventHasTheBatch(string name)
    {
        await CreateBatchAsync(name, 30000, 50);

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
    }

    [Given("que recebo Pix por uma integração AbacatePay ativa")]
    public async Task GivenIReceivePixThroughAbacatePay()
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{_slug}/payment-integrations", _token, new
        {
            providerCode = "ABACATEPAY",
            name = "AbacatePay - conta principal",
            credentials = new Dictionary<string, string> { ["API_KEY"] = "abc_dev_chave-de-teste-1234" },
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        await SendAsync(HttpMethod.Put, $"/api/v1/producers/{_slug}/payment-routes/PIX", _token, new { integrationIds = new[] { Read<IdResponse>().Id } });

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
    }

    [Given("que o evento já foi publicado")]
    public async Task GivenTheEventIsPublished()
    {
        await WhenIPublishTheEvent();

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
    }

    [Given("que a produtora {string} tem um evento com lotes")]
    public async Task GivenAnotherProducerHasAnEventWithBatches(string slug)
    {
        var (_, token) = await RegisterProducerAsync(slug);

        _foreignEventId = await CreateDraftEventAsync(slug, token, "Show Y");

        await SendAsync(HttpMethod.Post, BatchesRoute(slug, _foreignEventId), token, BatchBody("Pista", 8000, 100));

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
    }

    [When("crio o lote {string} de {int} centavos com {int} ingressos")]
    public Task WhenICreateTheBatch(string name, int priceCents, int quantity)
        => CreateBatchAsync(name, priceCents, quantity);

    [When("crio o lote {string} com a venda terminando depois do evento")]
    public Task WhenICreateTheBatchEndingAfterTheEvent(string name)
        => CreateBatchAsync(name, 10000, 100, EVENT_STARTS_AT.AddHours(10));

    [When("pauso o lote {string}")]
    public Task WhenIPauseTheBatch(string name)
        => SendAsync(HttpMethod.Put, $"{BatchesRoute(_slug, _eventId)}/{_batchesByName[name]}", _token, BatchBody(name, 30000, 50, status: "INACTIVE"));

    [When("publico o evento")]
    public Task WhenIPublishTheEvent()
        => SendAsync(HttpMethod.Post, $"/api/v1/producers/{_slug}/events/{_eventId}/publication", _token);

    [When("consulto os lotes do evento da {string} pela minha produtora")]
    public Task WhenIGetTheForeignBatches(string slug)
        => SendAsync(HttpMethod.Get, BatchesRoute(_slug, _foreignEventId), _token);

    [Then("o lote é criado com {int} ingressos disponíveis")]
    public void ThenTheBatchIsCreated(int available)
    {
        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
        Assert.Equal(available, Read<BatchResponse>().AvailableQuantity);
    }

    [Then("a operação é recusada com {string}")]
    public void ThenTheOperationIsRejected(string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }

    [Then("a operação conflita com {string}")]
    public void ThenTheOperationConflicts(string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }

    [Then("o lote {string} fica com o estado de venda {string}")]
    public async Task ThenTheBatchHasSaleState(string name, string saleState)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        await SendAsync(HttpMethod.Get, BatchesRoute(_slug, _eventId), _token);

        Assert.Equal(saleState, Assert.Single(Read<BatchResponse[]>(), batch => batch.Name == name).SaleState);
    }

    [Then("o evento fica publicado")]
    public void ThenTheEventIsPublished()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal("PUBLISHED", Read<EventResponse>().Status);
    }

    [Then("a publicação fica registrada na outbox")]
    public async Task ThenThePublicationIsInTheOutbox()
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_OUTBOX_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = _eventId }));

    [Then("o evento da outra produtora não é encontrado")]
    public void ThenTheForeignEventIsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == "EVENT_NOT_FOUND");
    }
}
