using Domain.ValueObjects;
using FunctionalTests.Support;
using Infra.Jwt.Base.TokenGenerator;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class CustomerAuthenticationSteps
{
    private const string CUSTOMERS_ROUTE = "api/v1/customers";
    private const string AUTHENTICATION_ROUTE = "api/v1/customers/authentication";

    private const string SELECT_CUSTOMER_ID_SQL = "SELECT id_customer FROM customers.Customer WHERE email = @email;";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.Producer (id_producer, name, email, phone, producer_type, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, 'Produtor', @email, '11987654321', 'COMPANY', @slug, 'ACTIVE', SYSUTCDATETIME(), NEWID());
        """;

    private const string INSERT_PRODUCER_CUSTOMER_SQL = """
        INSERT INTO producers.ProducerCustomer (id_producer_customer, producer_id, customer_id, created_at, created_correlation_id)
        VALUES (NEWID(), @producer_id, @customer_id, SYSUTCDATETIME(), NEWID());
        """;

    private const string FULL_NAME = "Maria da Silva";
    private const string EMAIL = "maria@ntix.com";
    private const string DOCUMENT = "52998224725";
    private const string PASSWORD = "Senha@123";
    private const string AUTHENTICATION_FAILED_MESSAGE = "Não foi possível autenticar com os dados informados.";

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private HttpResponseMessage? _response;
    private Guid _producerId;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAt);

    private Task<HttpResponseMessage> SendAsync(string route, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        return _client.SendAsync(request);
    }

    private async Task AuthenticateAsync(string? email, string? document, string password, Guid producerId)
        => _response = await SendAsync(AUTHENTICATION_ROUTE, new { email, document, password, producerId });

    private static async Task<Guid> CreateProducerAsync()
    {
        var producerId = Guid.NewGuid();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new SqlParameter("@id_producer", producerId),
            new SqlParameter("@email", $"{producerId:N}@ntix.com"),
            new SqlParameter("@slug", $"produtor-{producerId:N}"));

        return producerId;
    }

    [Given("que sou um cliente cadastrado na loja do produtor")]
    public async Task GivenCustomerLinkedToProducer()
    {
        var registration = await SendAsync(CUSTOMERS_ROUTE, new { fullName = FULL_NAME, email = EMAIL, document = DOCUMENT, password = PASSWORD });

        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var customerId = await NtixApiHooks.Database.ScalarAsync<Guid>(SELECT_CUSTOMER_ID_SQL, new SqlParameter("@email", EMAIL));

        _producerId = await CreateProducerAsync();

        await NtixApiHooks.Database.ExecuteAsync(
            INSERT_PRODUCER_CUSTOMER_SQL,
            new SqlParameter("@producer_id", _producerId),
            new SqlParameter("@customer_id", customerId));
    }

    [When("entro na loja com meu email e minha senha")]
    public Task WhenAuthenticatingWithEmail()
        => AuthenticateAsync(EMAIL, null, PASSWORD, _producerId);

    [When("entro na loja com meu CPF e minha senha")]
    public Task WhenAuthenticatingWithDocument()
        => AuthenticateAsync(null, DOCUMENT, PASSWORD, _producerId);

    [When("entro na loja com meu email e uma senha errada")]
    public Task WhenAuthenticatingWithWrongPassword()
        => AuthenticateAsync(EMAIL, null, "Errada@123", _producerId);

    [When("entro na loja com um email sem cadastro")]
    public Task WhenAuthenticatingWithUnknownEmail()
        => AuthenticateAsync("ninguem@ntix.com", null, PASSWORD, _producerId);

    [When("entro na loja de outro produtor com meu email e minha senha")]
    public async Task WhenAuthenticatingOnOtherProducer()
        => await AuthenticateAsync(EMAIL, null, PASSWORD, await CreateProducerAsync());

    [When("entro na loja informando meu email e meu CPF")]
    public Task WhenAuthenticatingWithEmailAndDocument()
        => AuthenticateAsync(EMAIL, DOCUMENT, PASSWORD, _producerId);

    [When("entro na loja informando apenas minha senha")]
    public Task WhenAuthenticatingOnlyWithPassword()
        => AuthenticateAsync(null, null, PASSWORD, _producerId);

    [Then("sou autenticado")]
    public async Task ThenAuthenticated()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var token = await _response.Content.ReadFromJsonAsync<TokenResponse>();

        Assert.False(string.IsNullOrWhiteSpace(token!.AccessToken));
        Assert.Equal("Bearer", token.TokenType);
    }

    [Then("meu acesso identifica a loja do produtor")]
    public async Task ThenTokenIdentifiesProducer()
    {
        var token = await ReadTokenAsync();

        Assert.Equal(_producerId.ToString(), token.GetClaim(JwtTokenGenerator.PRODUCER_ID_CLAIM).Value);
    }

    [Then("meu acesso dura {int} horas")]
    public async Task ThenTokenLasts(int hours)
    {
        var token = await ReadTokenAsync();

        Assert.Equal(TimeSpan.FromHours(hours), token.ValidTo - token.IssuedAt);
    }

    [Then("não sou autenticado por dados não reconhecidos")]
    public async Task ThenNotAuthenticated()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

        var notifications = await _response.Content.ReadFromJsonAsync<NotificationResponse[]>();

        Assert.Contains(notifications!, notification => notification.Message == AUTHENTICATION_FAILED_MESSAGE);
    }

    [Then("sou avisado de que {string}")]
    public async Task ThenWarnedThat(string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);

        var notifications = await _response.Content.ReadFromJsonAsync<NotificationResponse[]>();

        Assert.Contains(notifications!, notification => notification.Message == message);
    }

    private async Task<JsonWebToken> ReadTokenAsync()
    {
        var response = await _response!.Content.ReadFromJsonAsync<TokenResponse>();

        return new JsonWebToken(response!.AccessToken);
    }
}
