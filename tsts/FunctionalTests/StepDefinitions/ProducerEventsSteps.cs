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
public sealed class ProducerEventsSteps
{
    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @slug, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string PUBLISH_SQL = "UPDATE events.event SET status = 'PUBLISHED' WHERE id_event = @id_event;";

    private const string MOVE_TO_PAST_SQL = """
        UPDATE events.event
        SET starts_at = now() - interval '1 hour', doors_open_at = NULL, ends_at = now() + interval '5 hours'
        WHERE id_event = @id_event;
        """;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, (Guid Id, string Token)> _producersBySlug = [];
    private readonly Dictionary<string, EventResponse> _eventsByTitle = [];

    private string _slug = null!;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record EventResponse(Guid Id, string Slug, string Title, string Status, DateTime StartsAt);

    #region Apoio

    // O mesmo emissor do login (REQ5), sem passar pela senha: o login tem a própria feature.
    private static string IssueAccessToken(Guid producerId, string slug)
        => NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(
            producerUserId: Guid.NewGuid(),
            producerId: producerId,
            producerSlug: slug,
            userType: "ADMINISTRATOR",
            sessionId: Guid.NewGuid(),
            now: DateTime.UtcNow);

    private async Task<(Guid Id, string Token)> RegisterConnectedProducerAsync(string slug)
    {
        if (_producersBySlug.TryGetValue(slug, out var existing))
            return existing;

        var id = Guid.NewGuid();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{slug}@produtora.com" });

        var producer = (id, IssueAccessToken(id, slug));

        _producersBySlug[slug] = producer;

        return producer;
    }

    private static string Iso(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static object EventBody(string title, DateTime startsAt, DateTime? endsAt = null, string category = "FESTIVALS")
        => new
        {
            title,
            description = "Três palcos, um pôr do sol e nenhuma pressa.",
            category,
            startsAt = Iso(startsAt),
            endsAt = Iso(endsAt ?? startsAt.AddHours(9)),
            doorsOpenAt = Iso(startsAt.AddHours(-1)),
            venueName = "Parque das Dunas",
            address = "Av. Beira-Mar, 1200",
            city = "Recife",
            state = "PE",
            ageRating = "AGE_16",
            imageUrl = "https://cdn.exemplo.com/horizonte.jpg",
        };

    private static DateTime InDays(int days)
        => DateTime.UtcNow.Date.AddDays(days).AddHours(21);

    private async Task SendAsync(HttpMethod method, string route, object? body = null, string? token = null)
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

    private async Task<EventResponse> CreateEventAsAsync(string slug, string title, DateTime startsAt)
    {
        var producer = await RegisterConnectedProducerAsync(slug);

        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{slug}/events", EventBody(title, startsAt), producer.Token);

        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var created = Read<EventResponse>();

        _eventsByTitle[title] = created;

        return created;
    }

    private string MyToken
        => _producersBySlug[_slug].Token;

    #endregion

    [Given("que sou a produtora {string} e estou conectada à aba Eventos")]
    public async Task GivenIAmTheConnectedProducer(string slug)
    {
        _slug = slug;

        await RegisterConnectedProducerAsync(slug);
    }

    [Given("que a produtora {string} já criou o evento {string}")]
    public Task GivenAnotherProducerCreatedTheEvent(string slug, string title)
        => CreateEventAsAsync(slug, title, InDays(45));

    [Given("que criei o evento {string} daqui a {int} dias")]
    public Task GivenICreatedTheEvent(string title, int days)
        => CreateEventAsAsync(_slug, title, InDays(days));

    [Given("que criei o evento {string} daqui a {int} dias, já publicado")]
    public async Task GivenICreatedThePublishedEvent(string title, int days)
    {
        var created = await CreateEventAsAsync(_slug, title, InDays(days));

        await NtixApiHooks.Database.ExecuteAsync(PUBLISH_SQL, new NpgsqlParameter("@id_event", NpgsqlDbType.Uuid) { Value = created.Id });
    }

    [Given("que o evento {string} começou há 1 hora")]
    public Task GivenTheEventHasStarted(string title)
        => NtixApiHooks.Database.ExecuteAsync(MOVE_TO_PAST_SQL, new NpgsqlParameter("@id_event", NpgsqlDbType.Uuid) { Value = _eventsByTitle[title].Id });

    [When("crio o evento {string} daqui a {int} dias")]
    public async Task WhenICreateTheEvent(string title, int days)
    {
        await SendAsync(HttpMethod.Post, $"/api/v1/producers/{_slug}/events", EventBody(title, InDays(days)), MyToken);

        if (_response!.StatusCode == HttpStatusCode.Created)
            _eventsByTitle[title] = Read<EventResponse>();
    }

    [When("crio um evento que termina antes de começar")]
    public Task WhenICreateAnEventEndingBeforeItStarts()
        => SendAsync(HttpMethod.Post, $"/api/v1/producers/{_slug}/events", EventBody("Festival ao contrário", InDays(30), InDays(29)), MyToken);

    [When("crio o evento {string} na categoria {string}")]
    public Task WhenICreateTheEventInCategory(string title, string category)
        => SendAsync(HttpMethod.Post, $"/api/v1/producers/{_slug}/events", EventBody(title, InDays(30), category: category), MyToken);

    [When("mudo o título do evento para {string}")]
    public Task WhenIRenameTheEvent(string title)
    {
        var current = _eventsByTitle.Values.Single();

        return SendAsync(HttpMethod.Put, $"/api/v1/producers/{_slug}/events/{current.Id}", EventBody(title, InDays(60)), MyToken);
    }

    [When("listo meus eventos em rascunho")]
    public Task WhenIListMyDrafts()
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_slug}/events?status=DRAFT", token: MyToken);

    [When("consulto o evento {string}")]
    public Task WhenIGetTheEvent(string title)
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{_slug}/events/{_eventsByTitle[title].Id}", token: MyToken);

    [When("listo os eventos da produtora {string} sem estar conectada")]
    public Task WhenIListWithoutSession(string slug)
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{slug}/events");

    [When("listo os eventos da produtora {string}")]
    public Task WhenIListAnotherProducersEvents(string slug)
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{slug}/events", token: MyToken);

    [Then("o evento é criado")]
    public void ThenTheEventIsCreated()
    {
        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);
        Assert.Matches($"/api/v1/producers/{_slug}/events/[0-9a-f-]{{36}}$", _response.Headers.Location!.ToString());
    }

    [Then("o evento fica em rascunho com o endereço {string}")]
    public void ThenTheEventIsDraftWithSlug(string slug)
    {
        var created = Read<EventResponse>();

        Assert.Equal("DRAFT", created.Status);
        Assert.Equal(slug, created.Slug);
    }

    [Then("o endereço do evento é {string} com uma versão de 5 caracteres")]
    public void ThenTheSlugHasRandomVersion(string slug)
        => Assert.Matches($"^{slug}-[a-z0-9]{{5}}$", Read<EventResponse>().Slug);

    [Then("sou avisada com {string}")]
    public void ThenIAmWarnedWith(string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == code);
    }

    [Then("o evento passa a se chamar {string}")]
    public void ThenTheEventIsRenamed(string title)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(title, Read<EventResponse>().Title);
    }

    [Then("o endereço do evento continua {string}")]
    public void ThenTheSlugIsKept(string slug)
        => Assert.Equal(slug, Read<EventResponse>().Slug);

    [Then("vejo só o evento {string}")]
    public void ThenISeeOnlyTheEvent(string title)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(title, Assert.Single(Read<EventResponse[]>()).Title);
    }

    [Then("o evento não é encontrado")]
    public void ThenTheEventIsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == "EVENT_NOT_FOUND");
    }

    [Then("o acesso é negado por falta de sessão")]
    public void ThenAccessIsUnauthorized()
        => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [Then("o acesso é proibido")]
    public void ThenAccessIsForbidden()
        => Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);
}
