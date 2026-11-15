using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class ProducerAuthenticationSteps
{
    private const string SESSIONS_ROUTE = "api/v1/producers/sessions";
    private const string PRIVATE_KEY = "chave-de-teste";

    private const string INSERT_PRODUCER_WITH_ADMINISTRATOR_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @slug, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);

        INSERT INTO producers.producer_user (id_producer_user, id_producer, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id)
        VALUES (gen_random_uuid(), @id_producer, 'Pessoa Administradora', @email, @password_hash, @password_salt, 'ADMINISTRATOR', 'PENDING', now(), @id_producer);
        """;

    private const string UPDATE_USER_STATUS_SQL = "UPDATE producers.producer_user SET status = @status WHERE email = @email;";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    // O limite de tentativas é por e-mail e fica na memória da API, que sobe uma vez para todos os cenários. Cada
    // cenário usa o próprio endereço (ana+<cenário>@...), para as tentativas de um não contarem no outro.
    private readonly string _scenarioTag = Guid.NewGuid().ToString("N")[..12];

    private readonly List<HttpResponseMessage> _loginResponses = [];
    private readonly Dictionary<HttpResponseMessage, string> _bodies = [];

    private SessionResponse? _session;
    private string? _previousRefreshToken;
    private string? _lastEmail;
    private HttpResponseMessage? _response;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record UserResponse(Guid Id, string Name, string UserType, string Status);
    private sealed record ProducerResponse(Guid Id, string Slug, string Name, string Status);
    private sealed record SessionResponse(string AccessToken, int AccessTokenExpiresIn, string RefreshToken, int RefreshTokenExpiresIn, UserResponse User, ProducerResponse Producer);

    #region Apoio

    private string ScenarioEmail(string email)
    {
        var at = email.IndexOf('@');

        return $"{email[..at]}+{_scenarioTag}{email[at..]}";
    }

    private static void AddAuditableHeaders(HttpRequestMessage request)
    {
        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));
    }

    private async Task<HttpResponseMessage> PostAsync(string route, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };

        AddAuditableHeaders(request);

        var response = await _client.SendAsync(request);

        _bodies[response] = await response.Content.ReadAsStringAsync();

        return response;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
        => PostAsync(SESSIONS_ROUTE, new { email = ScenarioEmail(email), password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => PostAsync($"{SESSIONS_ROUTE}/refresh", new { refreshToken });

    private T Read<T>(HttpResponseMessage response)
        => JsonSerializer.Deserialize<T>(_bodies[response], JSON_OPTIONS)!;

    private NotificationResponse[] Notifications(HttpResponseMessage response)
        => Read<NotificationResponse[]>(response);

    private async Task ConnectAsync(string email)
    {
        var response = await LoginAsync(email, "Senha@123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _session = Read<SessionResponse>(response);
        _lastEmail = email;
    }

    private async Task UpdateStatusAsync(string email, string status)
        => await NtixApiHooks.Database.ExecuteAsync(
            UPDATE_USER_STATUS_SQL,
            new NpgsqlParameter("@status", NpgsqlDbType.Varchar) { Value = status },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = ScenarioEmail(email) });

    #endregion

    [Given("a produtora {string} com a administradora {string} e a senha {string}")]
    public async Task GivenProducerWithAdministrator(string slug, string email, string password)
    {
        var salt = PasswordSaltValueObject.Build();
        var hash = PasswordHashValueObject.Build(PasswordValueObject.Build(password), salt, PRIVATE_KEY);

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_WITH_ADMINISTRATOR_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = ScenarioEmail(email) },
            new NpgsqlParameter("@password_hash", NpgsqlDbType.Bytea) { Value = hash.Value },
            new NpgsqlParameter("@password_salt", NpgsqlDbType.Bytea) { Value = salt.Value });
    }

    [Given("que a administradora {string} ainda não verificou o e-mail")]
    public Task GivenAdministratorPending(string email)
        => UpdateStatusAsync(email, "PENDING");

    [Given("que a administradora {string} está bloqueada")]
    public Task GivenAdministratorBlocked(string email)
        => UpdateStatusAsync(email, "BLOCKED");

    [Given("que já houve {int} tentativas de login com {string} nos últimos 15 minutos")]
    public async Task GivenPreviousAttempts(int attempts, string email)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, "Errada@123")).StatusCode);
    }

    [Given("que a administradora {string} está conectada")]
    public Task GivenAdministratorConnected(string email)
        => ConnectAsync(email);

    [Given("que ela já renovou a sessão uma vez")]
    public async Task GivenAlreadyRefreshedOnce()
    {
        _previousRefreshToken = _session!.RefreshToken;

        var response = await RefreshAsync(_previousRefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        _session = Read<SessionResponse>(response);
    }

    [When("{string} entra com a senha {string}")]
    public async Task WhenLoggingIn(string email, string password)
    {
        _lastEmail = email;
        _response = await LoginAsync(email, password);
        _loginResponses.Add(_response);
    }

    [When("ela renova a sessão")]
    public async Task WhenRefreshing()
    {
        _previousRefreshToken = _session!.RefreshToken;
        _response = await RefreshAsync(_previousRefreshToken);
    }

    [When("a renovação anterior é apresentada de novo")]
    public async Task WhenPreviousRefreshTokenIsReused()
        => _response = await RefreshAsync(_previousRefreshToken!);

    [When("ela sai do painel")]
    public async Task WhenLoggingOut()
        => _response = await PostAsync($"{SESSIONS_ROUTE}/revoke", new { refreshToken = _session!.RefreshToken });

    [When("ela abre a aba Integrações da {string}")]
    public async Task WhenOpeningIntegrations(string slug)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/producers/{slug}/payment-integrations");

        AddAuditableHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);

        _response = await _client.SendAsync(request);
    }

    [When("alguém abre a aba Integrações da {string} sem sessão")]
    public async Task WhenOpeningIntegrationsWithoutSession(string slug)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/producers/{slug}/payment-integrations");

        AddAuditableHeaders(request);

        _response = await _client.SendAsync(request);
    }

    [Then("ela recebe uma sessão para usar o painel da {string}")]
    public void ThenReceivesSession(string slug)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        _session = Read<SessionResponse>(_response);

        Assert.False(string.IsNullOrWhiteSpace(_session.AccessToken));
        Assert.Equal(900, _session.AccessTokenExpiresIn);
        Assert.Equal(43, _session.RefreshToken.Length);
        Assert.Equal(604800, _session.RefreshTokenExpiresIn);
        Assert.Equal("PENDING", _session.User.Status);
        Assert.Equal(slug, _session.Producer.Slug);
    }

    [Then("a sessão não revela o e-mail dela")]
    public void ThenSessionDoesNotRevealEmail()
    {
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(_session!.AccessToken.Split('.')[1]));

        Assert.DoesNotContain("@", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(ScenarioEmail(_lastEmail!), payload, StringComparison.OrdinalIgnoreCase);
    }

    [Then("as duas tentativas são recusadas com a mesma mensagem de e-mail ou senha incorretos")]
    public void ThenBothAttemptsAreRejectedTheSameWay()
    {
        Assert.Equal(2, _loginResponses.Count);
        Assert.All(_loginResponses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));

        var bodies = _loginResponses.Select(response => _bodies[response]).Distinct().ToArray();
        Assert.Single(bodies);
        Assert.Equal("INVALID_CREDENTIALS", Assert.Single(Notifications(_loginResponses[0])).Code);
    }

    [Then("ela é avisada de que o acesso está bloqueado")]
    public void ThenBlocked()
    {
        Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);
        Assert.Equal("PRODUCER_USER_BLOCKED", Assert.Single(Notifications(_response)).Code);
    }

    [Then("ela é avisada de que houve tentativas demais")]
    public void ThenTooManyAttempts()
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, _response!.StatusCode);
        Assert.Equal("TOO_MANY_LOGIN_ATTEMPTS", Assert.Single(Notifications(_response)).Code);
    }

    [Then("fica sabendo em quanto tempo pode tentar de novo")]
    public void ThenKnowsWhenToRetry()
    {
        var retryAfter = _response!.Headers.RetryAfter?.Delta;

        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15));
    }

    [Then("ela recebe uma sessão nova")]
    public void ThenReceivesNewSession()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var renewed = Read<SessionResponse>(_response);

        Assert.NotEqual(_session!.RefreshToken, renewed.RefreshToken);
        Assert.NotEqual(_session.AccessToken, renewed.AccessToken);

        _session = renewed;
    }

    [Then("a renovação anterior deixa de funcionar")]
    public async Task ThenPreviousRefreshTokenStopsWorking()
    {
        var response = await RefreshAsync(_previousRefreshToken!);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("INVALID_REFRESH_TOKEN", Assert.Single(Notifications(response)).Code);
    }

    [Then("a renovação é recusada")]
    public void ThenRefreshIsRejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);
        Assert.Equal("INVALID_REFRESH_TOKEN", Assert.Single(Notifications(_response)).Code);
    }

    [Then("a renovação mais nova também deixa de funcionar")]
    [Then("a renovação dela deixa de funcionar")]
    public async Task ThenCurrentRefreshTokenStopsWorking()
    {
        var response = await RefreshAsync(_session!.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Then("a saída é confirmada")]
    public void ThenLogoutConfirmed()
        => Assert.Equal(HttpStatusCode.NoContent, _response!.StatusCode);

    [Then("o painel atende")]
    public void ThenPanelAnswers()
        => Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

    [Then("o painel pede para entrar")]
    public void ThenPanelAsksToLogIn()
        => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [Then("o painel recusa por ser de outra produtora")]
    public void ThenPanelRefusesOtherProducer()
        => Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);
}
