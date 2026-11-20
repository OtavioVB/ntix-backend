using Application.UseCases.Base.Authentication;
using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class CustomerAuthenticationSteps
{
    private const string PASSWORD = "Senha@123";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly List<HttpResponseMessage> _responses = [];

    private string _email = null!;
    private string _cpf = null!;
    private SessionResponse? _session;
    private string? _producerToken;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;

    private sealed record CustomerResponse(Guid Id, string FirstName);
    private sealed record SessionResponse(string AccessToken, int AccessTokenExpiresIn, string RefreshToken, int RefreshTokenExpiresIn, CustomerResponse Customer);
    private sealed record MeResponse(Guid Id, string FullName, string Email, string Document);
    private sealed record NotificationResponse(string Code, string Message, string Type);

    #region Apoio

    // CPF válido e único por cenário: o limite de tentativas vive na API durante toda a execução.
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

    private string MaskedCpf
        => $"{_cpf[..3]}.{_cpf[3..6]}.{_cpf[6..9]}-{_cpf[9..]}";

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
        _responses.Add(_response);
    }

    private T Read<T>()
        => JsonSerializer.Deserialize<T>(_body, JSON_OPTIONS)!;

    private Task LoginAsync(string login, string password)
        => SendAsync(HttpMethod.Post, "/api/v1/customers/sessions", new { login, password });

    #endregion

    [Given("que a cliente {string} tem conta com o e-mail {string}")]
    public async Task GivenCustomerAccount(string fullName, string email)
    {
        _email = email;
        _cpf = NewCpf();

        await SendAsync(HttpMethod.Post, "/api/v1/customers", new { fullName, email, document = _cpf, password = PASSWORD });

        Assert.True(_response!.IsSuccessStatusCode, _body);
    }

    [Given("que ela errou a senha {int} vezes com o CPF")]
    public async Task GivenWrongAttempts(int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
            await LoginAsync(_cpf, "Senha@errada1");
    }

    [Given("que ela está conectada")]
    public async Task GivenSheIsConnected()
    {
        await LoginAsync(_email, PASSWORD);

        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        _session = Read<SessionResponse>();
    }

    [Given("que o usuário da produtora {string} está conectado ao painel")]
    public void GivenProducerIsConnected(string slug)
        => _producerToken = NtixApiHooks.Factory.Services.GetRequiredService<ProducerAccessTokenIssuer>().Issue(Guid.NewGuid(), Guid.NewGuid(), slug, "ADMINISTRATOR", Guid.NewGuid(), DateTime.UtcNow);

    [When("ela entra com o e-mail e a senha correta")]
    public Task WhenSheLogsInByEmail()
        => LoginAsync(_email, PASSWORD);

    [When("ela entra com o CPF com máscara e a senha correta")]
    public Task WhenSheLogsInByMaskedCpf()
        => LoginAsync(MaskedCpf, PASSWORD);

    [When("alguém entra com {string} e a senha correta")]
    public Task WhenSomeoneLogsIn(string login)
        => LoginAsync(login, PASSWORD);

    [When("ela entra com o e-mail e a senha {string}")]
    public Task WhenSheLogsInWithPassword(string password)
        => LoginAsync(_email, password);

    [When("a cliente renova a sessão")]
    public Task WhenSheRefreshes()
        => SendAsync(HttpMethod.Post, "/api/v1/customers/sessions/refresh", new { refreshToken = _session!.RefreshToken });

    [When("ela consulta os próprios dados")]
    public Task WhenSheGetsHerData()
        => SendAsync(HttpMethod.Get, "/api/v1/customers/me", token: _session!.AccessToken);

    [When("ela tenta abrir os eventos da produtora {string} com o token dela")]
    public Task WhenSheOpensProducerPanel(string slug)
        => SendAsync(HttpMethod.Get, $"/api/v1/producers/{slug}/events", token: _session!.AccessToken);

    [When("ele consulta os dados do cliente com o token dele")]
    public Task WhenProducerGetsCustomerData()
        => SendAsync(HttpMethod.Get, "/api/v1/customers/me", token: _producerToken);

    [Then("recebe a sessão com access token e refresh token")]
    public void ThenReceivesSession()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var session = Read<SessionResponse>();

        Assert.False(string.IsNullOrEmpty(session.AccessToken));
        Assert.Equal(43, session.RefreshToken.Length);
        Assert.Equal(900, session.AccessTokenExpiresIn);
    }

    [Then("o login devolve só o primeiro nome {string}")]
    public void ThenOnlyFirstName(string firstName)
    {
        Assert.Equal(firstName, Read<SessionResponse>().Customer.FirstName);
        Assert.DoesNotContain("Souza", _body);
    }

    [Then("as duas tentativas são recusadas como credencial inválida")]
    public async Task ThenBothAreInvalidCredentials()
    {
        var lastTwo = _responses.TakeLast(2).ToArray();

        foreach (var response in lastTwo)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("INVALID_CREDENTIALS", await response.Content.ReadAsStringAsync());
        }
    }

    [Then("o login é recusado pelo limite de tentativas, com o tempo de espera")]
    public void ThenRateLimited()
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, _response!.StatusCode);
        Assert.Contains(Read<NotificationResponse[]>(), notification => notification.Code == "TOO_MANY_LOGIN_ATTEMPTS");
        Assert.True(_response.Headers.RetryAfter!.Delta > TimeSpan.Zero);
    }

    [Then("recebe um novo par de tokens")]
    public void ThenNewPair()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.NotEqual(_session!.RefreshToken, Read<SessionResponse>().RefreshToken);
    }

    [Then("o refresh token anterior deixa de funcionar")]
    public async Task ThenOldTokenFails()
    {
        await SendAsync(HttpMethod.Post, "/api/v1/customers/sessions/refresh", new { refreshToken = _session!.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);
    }

    [Then("vê o CPF ofuscado")]
    public void ThenObfuscatedCpf()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var me = Read<MeResponse>();

        Assert.Equal($"***.{_cpf[3..6]}.{_cpf[6..9]}-**", me.Document);
        Assert.Equal(_email, me.Email);
    }

    [Then("o acesso é recusado com 403")]
    public void ThenForbidden()
        => Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);
}
