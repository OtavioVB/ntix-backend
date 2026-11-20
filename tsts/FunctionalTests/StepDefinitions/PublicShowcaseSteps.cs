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
public sealed class PublicShowcaseSteps
{
    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);
    private static readonly DateTime EVENT_STARTS_AT = DateTime.UtcNow.Date.AddDays(60).AddHours(21);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, string> _tokensBySlug = [];
    private readonly Dictionary<string, (string ProducerSlug, Guid Id, string Slug)> _eventsByTitle = [];

    private HttpResponseMessage? _response;
    private string _body = string.Empty;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record IdResponse(Guid Id);
    private sealed record EventCreatedResponse(Guid Id, string Slug);
    private sealed record ProducerResponse(string Slug, string Name);
    private sealed record SummaryResponse(string Slug, string Title, long? PriceFromCents, string SaleState, ProducerResponse Producer);
    private sealed record PageResponse(SummaryResponse[] Items, int Page, int PageSize, int TotalItems);
    private sealed record PublicBatchResponse(Guid Id, string Name, string SaleState, int MaxSelectable);
    private sealed record PublicEventResponse(string Slug, string Title, PublicBatchResponse[] Batches);

    #region Apoio

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private async Task<string> TokenOfAsync(string slug)
    {
        if (_tokensBySlug.TryGetValue(slug, out var existing))
            return existing;

        var id = Guid.NewGuid();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = $"Produtora {slug}" },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        // O mesmo emissor do login (REQ5), sem passar pela senha.
        var token = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(Guid.NewGuid(), id, slug, "ADMINISTRATOR", Guid.NewGuid(), DateTime.UtcNow);

        _tokensBySlug[slug] = token;

        return token;
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

    private async Task CreateEventAsync(string producerSlug, string title, string city, string state)
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/events", await TokenOfAsync(producerSlug), new
        {
            title,
            category = "FESTIVALS",
            startsAt = Iso(EVENT_STARTS_AT),
            endsAt = Iso(EVENT_STARTS_AT.AddHours(9)),
            venueName = "Parque das Dunas",
            address = "Av. Beira-Mar, 1200",
            city,
            state,
            ageRating = "AGE_16",
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var created = Read<EventCreatedResponse>();

        _eventsByTitle[title] = (producerSlug, created.Id, created.Slug);
    }

    private async Task CreateBatchAsync(string title, string name, long priceCents, string status = "ACTIVE")
    {
        var (producerSlug, eventId, _) = _eventsByTitle[title];

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{producerSlug}/events/{eventId}/batches", await TokenOfAsync(producerSlug), new
        {
            name,
            priceCents,
            quantity = 500,
            salesStartsAt = Iso(DateTime.UtcNow.AddDays(-1)),
            salesEndsAt = Iso(EVENT_STARTS_AT),
            status,
        });

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
    }

    private async Task PublishAsync(string title)
    {
        var (producerSlug, eventId, _) = _eventsByTitle[title];
        var token = await TokenOfAsync(producerSlug);

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

    #endregion

    [Given(@"^que a produtora ""([^""]*)"" publicou o evento ""([^""]*)"" em ([^-]+)-([A-Z]{2}) com o lote ""([^""]*)"" de (\d+) centavos$")]
    public async Task GivenPublishedEvent(string producerSlug, string title, string city, string state, string batchName, long priceCents)
    {
        await CreateEventAsync(producerSlug, title, city, state);
        await CreateBatchAsync(title, batchName, priceCents);
        await PublishAsync(title);
    }

    [Given("que a produtora {string} tem o evento {string} em rascunho")]
    public Task GivenDraftEvent(string producerSlug, string title)
        => CreateEventAsync(producerSlug, title, "Recife", "PE");

    [Given("que o lote {string} do evento {string} está pausado")]
    public Task GivenPausedBatch(string batchName, string title)
        => CreateBatchAsync(title, batchName, 30000, "INACTIVE");

    [When("o comprador abre o hub")]
    public Task WhenTheBuyerOpensTheHub()
        => SendAsync(HttpMethod.Get, "/api/v1/events");

    [When("o comprador filtra a UF {string}")]
    public Task WhenTheBuyerFiltersByState(string state)
        => SendAsync(HttpMethod.Get, $"/api/v1/events?state={state}");

    [When("o comprador abre a loja da {string}")]
    public Task WhenTheBuyerOpensTheStore(string producerSlug)
        => SendAsync(HttpMethod.Get, $"/api/v1/events?producer={producerSlug}");

    [When("o comprador abre a página do evento {string}")]
    public Task WhenTheBuyerOpensTheEvent(string title)
        => SendAsync(HttpMethod.Get, $"/api/v1/events/{_eventsByTitle[title].Slug}");

    [When("o comprador pede {int} eventos por página")]
    public Task WhenTheBuyerAsksForPageSize(int pageSize)
        => SendAsync(HttpMethod.Get, $"/api/v1/events?pageSize={pageSize}");

    [Then("vê o evento {string} a partir de {int} centavos")]
    public void ThenSeesTheEventFrom(string title, int priceCents)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var item = Assert.Single(Read<PageResponse>().Items, summary => summary.Title == title);

        Assert.Equal(priceCents, item.PriceFromCents);
        Assert.Equal("ON_SALE", item.SaleState);
        Assert.Equal("mare-cultural", item.Producer.Slug);
    }

    [Then("não vê o evento {string}")]
    public void ThenDoesNotSeeTheEvent(string title)
        => Assert.DoesNotContain(Read<PageResponse>().Items, summary => summary.Title == title);

    [Then("a resposta pode ficar em cache por 15 segundos")]
    public void ThenTheResponseIsCacheable()
        => Assert.Equal("public, max-age=15", _response!.Headers.CacheControl!.ToString());

    [Then("a lista vem vazia")]
    public void ThenTheListIsEmpty()
    {
        var page = Read<PageResponse>();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Then("vê só o evento {string}")]
    public void ThenSeesOnlyTheEvent(string title)
        => Assert.Equal(title, Assert.Single(Read<PageResponse>().Items).Title);

    [Then("vê o lote {string} em venda com até {int} ingressos selecionáveis")]
    public void ThenSeesTheBatchOnSale(string name, int maxSelectable)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var batch = Assert.Single(Read<PublicEventResponse>().Batches, item => item.Name == name);

        Assert.Equal("ON_SALE", batch.SaleState);
        Assert.Equal(maxSelectable, batch.MaxSelectable);
    }

    [Then("a página não mostra a quantidade total do lote")]
    public void ThenThePageHidesStock()
    {
        Assert.DoesNotContain("\"quantity\"", _body);
        Assert.DoesNotContain("soldQuantity", _body);
        Assert.DoesNotContain("availableQuantity", _body);
    }

    [Then("não vê o lote {string}")]
    public void ThenDoesNotSeeTheBatch(string name)
        => Assert.DoesNotContain(Read<PublicEventResponse>().Batches, batch => batch.Name == name);

    [Then("a página do evento não é encontrada")]
    public void ThenTheEventIsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == "EVENT_NOT_FOUND");
    }

    [Then("a consulta é recusada com {string}")]
    public void ThenTheQueryIsRejected(string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }
}
