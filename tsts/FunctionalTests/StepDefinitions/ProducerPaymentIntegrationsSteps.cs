using Application.UseCases.Base.Interfaces;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WebApi.Authentication;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class ProducerPaymentIntegrationsSteps
{
    private const string ABACATEPAY = "ABACATEPAY";
    private const string TEST_PROVIDER = "FORNECEDOR_TESTE";
    private const string API_KEY = "abc_dev_chave-de-teste-1234";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @slug, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string INSERT_TEST_PROVIDER_SQL = """
        INSERT INTO payments.provider (id_provider, code, name, status) VALUES (@id_provider, 'FORNECEDOR_TESTE', 'Fornecedor de teste', 'ENABLED');
        INSERT INTO payments.provider_payment_method (id_provider, payment_method) VALUES (@id_provider, 'PIX');
        INSERT INTO payments.provider_credential_key (id_provider, credential_key, is_secret, is_required) VALUES (@id_provider, 'API_KEY', TRUE, TRUE);
        """;

    private const string COUNT_ABACATEPAY_METHODS_SQL = """
        SELECT COUNT(*)::int
        FROM payments.provider p
        JOIN payments.provider_payment_method m ON m.id_provider = p.id_provider
        WHERE p.code = 'ABACATEPAY' AND p.status = 'ENABLED';
        """;

    private const string COUNT_INTEGRATIONS_SQL = "SELECT COUNT(*)::int FROM payments.producer_integration;";

    private const string SELECT_ENCRYPTED_VALUES_SQL = """
        SELECT string_agg(encode(encrypted_value, 'escape'), '|')
        FROM payments.producer_integration_credential
        WHERE id_producer_integration = @id_producer_integration;
        """;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();
    private readonly Dictionary<string, Guid> _integrationsByName = [];
    private readonly Dictionary<string, (Guid Id, string Token)> _producersBySlug = [];

    private string _slug = null!;
    private HttpResponseMessage? _response;
    private string? _lastApiKey;

    private sealed record NotificationResponse(string Code, string Message, string Type);
    private sealed record CredentialResponse(string Key, bool IsSecret, string? ValueHint, string? Value);
    private sealed record IntegrationResponse(Guid Id, string ProviderCode, string Name, string Status, CredentialResponse[] Credentials);
    private sealed record RouteItemResponse(int Priority, Guid IntegrationId, string Name, string Status);

    #region Apoio

    private static string ToPaymentMethod(string method)
        => method switch
        {
            "Pix" => "PIX",
            "boleto" => "BOLETO",
            "cartão de crédito" => "CREDIT_CARD",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Método de pagamento desconhecido no cenário."),
        };

    // Faz o papel do login do REQ5: o token carrega só identificadores da usuária e da produtora.
    private static string IssueAccessToken(Guid producerId, string slug)
    {
        var jwtConfiguration = NtixApiHooks.Factory.Services.GetRequiredService<JwtConfiguration>();

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwtConfiguration.Issuer,
            Audience = jwtConfiguration.Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [ProducerClaims.SUBJECT] = Guid.NewGuid().ToString(),
                [ProducerClaims.PRODUCER_ID] = producerId.ToString(),
                [ProducerClaims.PRODUCER_SLUG] = slug,
                [ProducerClaims.USER_TYPE] = "ADMINISTRATOR",
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(NtixApiHooks.JWT_SIGNING_KEY)), SecurityAlgorithms.HmacSha256),
        });
    }

    private async Task<(Guid Id, string Token)> RegisterConnectedProducerAsync(string slug)
    {
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

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, object? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, route);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        return _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ConnectAsync(string slug, string providerCode, string name, Dictionary<string, string?> credentials)
    {
        var response = await SendAsync(HttpMethod.Post, $"api/v1/producers/{slug}/payment-integrations", new { providerCode, name, credentials }, _producersBySlug[slug].Token);

        if (response.StatusCode == HttpStatusCode.Created)
            _integrationsByName[name] = (await response.Content.ReadFromJsonAsync<IntegrationResponse>(JSON_OPTIONS))!.Id;

        return response;
    }

    private async Task GivenActiveIntegrationAsync(string slug, string providerCode, string name)
        => Assert.Equal(HttpStatusCode.Created, (await ConnectAsync(slug, providerCode, name, new() { ["API_KEY"] = API_KEY })).StatusCode);

    private Task<HttpResponseMessage> DefineRouteAsync(string paymentMethod, params string[] integrationNames)
        => SendAsync(
            HttpMethod.Put,
            $"api/v1/producers/{_slug}/payment-routes/{paymentMethod}",
            new { integrationIds = integrationNames.Select(name => _integrationsByName[name].ToString()).ToArray() },
            _producersBySlug[_slug].Token);

    private async Task<Domain.CrossCutting.MethodResultContext.MethodResult<RankPaymentIntegrationsUseCaseOutput>> RankAsync(string paymentMethod)
    {
        // O ranqueamento não tem endpoint neste requisito: é o caso de uso que o pagamento (REQ7) vai chamar.
        using var scope = NtixApiHooks.Factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>>()
            .ExecuteAsync(
                RankPaymentIntegrationsUseCaseInput.Create(_producersBySlug[_slug].Id, paymentMethod),
                AuditableInfoValueObject.Build(Guid.NewGuid().ToString(), "functional-tests", null, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture)));
    }

    private async Task ThenRejectedWithAsync(HttpStatusCode statusCode, string notificationCode)
    {
        Assert.Equal(statusCode, _response!.StatusCode);

        var notifications = await _response.Content.ReadFromJsonAsync<NotificationResponse[]>(JSON_OPTIONS);

        Assert.Contains(notifications!, notification => notification.Code == notificationCode);
    }

    #endregion

    #region Dado

    [Given("que sou a produtora {string} e estou conectada ao painel")]
    public async Task GivenConnectedProducer(string slug)
    {
        await RegisterConnectedProducerAsync(slug);

        _slug = slug;
    }

    [Given("o catálogo da Ntix oferece o AbacatePay para Pix, boleto e cartão de crédito")]
    public async Task GivenAbacatePayInCatalog()
        => Assert.Equal(3, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_ABACATEPAY_METHODS_SQL));

    [Given("que tenho as integrações ativas {string} e {string}")]
    public async Task GivenTwoActiveIntegrations(string first, string second)
    {
        await GivenActiveIntegrationAsync(_slug, ABACATEPAY, first);
        await GivenActiveIntegrationAsync(_slug, ABACATEPAY, second);
    }

    [Given("o Pix está definido pela {string} e depois pela {string}")]
    public async Task GivenPixRoute(string first, string second)
        => Assert.Equal(HttpStatusCode.OK, (await DefineRouteAsync("PIX", first, second)).StatusCode);

    [Given("que o catálogo também oferece um fornecedor de teste só para Pix")]
    public Task GivenTestProvider()
        => NtixApiHooks.Database.ExecuteAsync(INSERT_TEST_PROVIDER_SQL, new NpgsqlParameter("@id_provider", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() });

    [Given("que tenho a integração ativa {string} nesse fornecedor")]
    public Task GivenTestProviderIntegration(string name)
        => GivenActiveIntegrationAsync(_slug, TEST_PROVIDER, name);

    [Given("que tenho a integração ativa {string} no AbacatePay")]
    public Task GivenAbacatePayIntegration(string name)
        => GivenActiveIntegrationAsync(_slug, ABACATEPAY, name);

    [Given("que a produtora {string} tem a integração ativa {string}")]
    public async Task GivenOtherProducerIntegration(string slug, string name)
    {
        await RegisterConnectedProducerAsync(slug);
        await GivenActiveIntegrationAsync(slug, ABACATEPAY, name);
    }

    [Given("que a produtora {string} está conectada ao painel")]
    public Task GivenOtherConnectedProducer(string slug)
        => RegisterConnectedProducerAsync(slug);

    #endregion

    #region Quando

    [When("conecto o AbacatePay como {string} com a chave de API {string}")]
    public async Task WhenConnectingAbacatePay(string name, string apiKey)
    {
        _lastApiKey = apiKey;
        _response = await ConnectAsync(_slug, ABACATEPAY, name, new() { ["API_KEY"] = apiKey });
    }

    [When("conecto o AbacatePay como {string} sem a chave de API")]
    public async Task WhenConnectingWithoutApiKey(string name)
        => _response = await ConnectAsync(_slug, ABACATEPAY, name, new() { ["WEBHOOK_SECRET"] = "segredo-do-webhook-com-mais-de-32-caracteres" });

    [When("conecto o AbacatePay como {string} informando também a credencial {string}")]
    public async Task WhenConnectingWithUnknownCredential(string name, string credentialKey)
        => _response = await ConnectAsync(_slug, ABACATEPAY, name, new() { ["API_KEY"] = API_KEY, [credentialKey] = "123456" });

    [When(@"^defino o (Pix|boleto|cartão de crédito) pela ""([^""]*)""$")]
    public async Task WhenDefiningRoute(string method, string integrationName)
        => _response = await DefineRouteAsync(ToPaymentMethod(method), integrationName);

    [When(@"^defino o Pix pela ""([^""]*)"" e depois pela ""([^""]*)""$")]
    public async Task WhenDefiningPixRouteWithTwo(string first, string second)
        => _response = await DefineRouteAsync("PIX", first, second);

    [When("inativo a integração {string}")]
    public async Task WhenInactivating(string name)
    {
        _response = await SendAsync(HttpMethod.Put, $"api/v1/producers/{_slug}/payment-integrations/{_integrationsByName[name]}", new { status = "INACTIVE" }, _producersBySlug[_slug].Token);

        Assert.Equal(HttpStatusCode.OK, _response.StatusCode);
    }

    [When("a Ntix desliga o fornecedor de teste")]
    public Task WhenNtixDisablesTestProvider()
        => NtixApiHooks.Database.ExecuteAsync("UPDATE payments.provider SET status = 'DISABLED' WHERE code = 'FORNECEDOR_TESTE';");

    [When("alguém consulta as integrações da {string} sem estar conectado")]
    public async Task WhenAnonymousQueries(string slug)
        => _response = await SendAsync(HttpMethod.Get, $"api/v1/producers/{slug}/payment-integrations");

    [When("ela consulta as integrações da {string}")]
    public async Task WhenOtherProducerQueries(string slug)
        => _response = await SendAsync(HttpMethod.Get, $"api/v1/producers/{slug}/payment-integrations", token: _producersBySlug.Single(producer => producer.Key != slug).Value.Token);

    #endregion

    #region Então

    [Then("a integração {string} fica ativa")]
    public async Task ThenIntegrationIsActive(string name)
    {
        Assert.Equal(HttpStatusCode.Created, _response!.StatusCode);

        var integration = await _response.Content.ReadFromJsonAsync<IntegrationResponse>(JSON_OPTIONS);

        Assert.Equal(name, integration!.Name);
        Assert.Equal("ACTIVE", integration.Status);
        Assert.Equal(ABACATEPAY, integration.ProviderCode);
        Assert.EndsWith($"/api/v1/producers/{_slug}/payment-integrations/{integration.Id}", _response.Headers.Location!.ToString());
    }

    [Then("vejo da chave de API só o final {string}")]
    public async Task ThenSeeOnlyApiKeyHint(string hint)
    {
        var body = await _response!.Content.ReadAsStringAsync();
        var integration = JsonSerializer.Deserialize<IntegrationResponse>(body, JSON_OPTIONS);
        var apiKey = Assert.Single(integration!.Credentials);

        Assert.Equal(hint, apiKey.ValueHint);
        Assert.Null(apiKey.Value);
        Assert.DoesNotContain(_lastApiKey!, body);
    }

    [Then("a chave de API não fica guardada como foi escrita")]
    public async Task ThenApiKeyIsEncrypted()
    {
        var stored = await NtixApiHooks.Database.ScalarAsync<string>(
            SELECT_ENCRYPTED_VALUES_SQL,
            new NpgsqlParameter("@id_producer_integration", NpgsqlDbType.Uuid) { Value = _integrationsByName.Values.Single() });

        Assert.DoesNotContain(_lastApiKey!, stored);
        Assert.DoesNotContain("chave-secreta", stored);
    }

    [Then("sou avisada de que falta uma credencial obrigatória")]
    public Task ThenWarnedRequiredCredential()
        => ThenRejectedWithAsync(HttpStatusCode.BadRequest, "CREDENTIAL_KEY_REQUIRED");

    [Then("sou avisada de que informei uma credencial que o fornecedor não usa")]
    public Task ThenWarnedUnknownCredential()
        => ThenRejectedWithAsync(HttpStatusCode.BadRequest, "CREDENTIAL_KEY_UNKNOWN");

    [Then("nenhuma integração é criada")]
    public async Task ThenNoIntegrationIsCreated()
        => Assert.Equal(0, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_INTEGRATIONS_SQL));

    [Then(@"^o (Pix|boleto|cartão de crédito) é cobrado na ordem ""([^""]*)""$")]
    public async Task ThenChargedInOrder(string method, string integrationNames)
    {
        Assert.True(_response is null || _response.IsSuccessStatusCode);

        var ranking = await RankAsync(ToPaymentMethod(method));
        var expected = integrationNames.Split(',', StringSplitOptions.TrimEntries).Select(name => _integrationsByName[name]);

        Assert.True(ranking.IsSuccess);
        Assert.Equal(expected, ranking.Output!.Integrations.Select(integration => integration.IntegrationId));
    }

    [Then("o Pix continua listando a {string} como inativa")]
    public async Task ThenPixRouteStillListsInactive(string name)
    {
        var response = await SendAsync(HttpMethod.Get, $"api/v1/producers/{_slug}/payment-routes", token: _producersBySlug[_slug].Token);
        var routes = await response.Content.ReadFromJsonAsync<Dictionary<string, RouteItemResponse[]>>(JSON_OPTIONS);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["BOLETO", "CREDIT_CARD", "PIX"], routes!.Keys.Order());

        var item = Assert.Single(routes["PIX"], route => route.IntegrationId == _integrationsByName[name]);
        Assert.Equal(1, item.Priority);
        Assert.Equal("INACTIVE", item.Status);
    }

    [Then("o boleto não está disponível para os compradores")]
    public async Task ThenBoletoIsUnavailable()
    {
        var ranking = await RankAsync("BOLETO");

        Assert.True(ranking.IsError);
        Assert.Contains(ranking.Notifications, notification => notification.Code == "PAYMENT_METHOD_UNAVAILABLE");
    }

    [Then("sou avisada de que uma integração só pode aparecer uma vez no método")]
    public Task ThenWarnedDuplicated()
        => ThenRejectedWithAsync(HttpStatusCode.BadRequest, "INTEGRATION_DUPLICATED_IN_ROUTE");

    [Then("sou avisada de que o fornecedor não aceita esse método")]
    public Task ThenWarnedUnsupportedMethod()
        => ThenRejectedWithAsync(HttpStatusCode.BadRequest, "PROVIDER_DOES_NOT_SUPPORT_METHOD");

    [Then("sou avisada de que a integração não existe para a minha produtora")]
    public Task ThenWarnedIntegrationNotFound()
        => ThenRejectedWithAsync(HttpStatusCode.BadRequest, "INTEGRATION_NOT_FOUND");

    [Then("o acesso é negado por falta de autenticação")]
    public void ThenUnauthorized()
        => Assert.Equal(HttpStatusCode.Unauthorized, _response!.StatusCode);

    [Then("o acesso é negado por ser de outra produtora")]
    public void ThenForbidden()
        => Assert.Equal(HttpStatusCode.Forbidden, _response!.StatusCode);

    #endregion
}
