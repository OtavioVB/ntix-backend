using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.Data.SqlClient;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class CustomerRegistrationSteps
{
    private const string CUSTOMERS_ROUTE = "api/v1/customers";
    private const string COUNT_CUSTOMERS_SQL = "SELECT COUNT(*) FROM customers.Customer;";
    private const string SELECT_SLUG_BY_EMAIL_SQL = "SELECT slug FROM customers.Customer WHERE email = @email;";

    private static readonly Dictionary<string, string> AUDITABLE_HEADERS_BY_INFORMATION = new()
    {
        ["o identificador de correlação"] = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY,
        ["o serviço de origem"] = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY,
        ["o usuário executor"] = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY,
        ["o momento da solicitação"] = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY,
    };

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private HttpResponseMessage? _response;
    private string? _registeredEmail;
    private int _customersBeforeRegistration;

    private sealed record NotificationResponse(string Code, string Message, string Type);

    private Task<HttpResponseMessage> RegisterAsync(string fullName, string email, string document, string? omittedHeader = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, CUSTOMERS_ROUTE)
        {
            Content = JsonContent.Create(new { fullName, email, document })
        };

        var auditableHeaders = new Dictionary<string, string>
        {
            [AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY] = Guid.NewGuid().ToString(),
            [AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY] = "functional-tests",
            [AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY] = "user-123",
            [AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY] = DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture),
        };

        foreach (var (header, value) in auditableHeaders.Where(auditableHeader => auditableHeader.Key != omittedHeader))
            request.Headers.Add(header, value);

        return _client.SendAsync(request);
    }

    private static Task<int> CountCustomersAsync()
        => NtixApiHooks.Database.ScalarAsync<int>(COUNT_CUSTOMERS_SQL);

    private Task<string> GetRegisteredSlugAsync()
        => NtixApiHooks.Database.ScalarAsync<string>(
            SELECT_SLUG_BY_EMAIL_SQL,
            new SqlParameter("@email", _registeredEmail!.Trim().ToLowerInvariant()));

    [Given("que já existe um cliente com o nome {string}, o email {string} e o documento {string}")]
    public async Task GivenExistingCustomer(string fullName, string email, string document)
    {
        var response = await RegisterAsync(fullName, email, document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [When("me cadastro com o nome {string}, o email {string} e o documento {string}")]
    public async Task WhenRegistering(string fullName, string email, string document)
    {
        _customersBeforeRegistration = await CountCustomersAsync();
        _registeredEmail = email;
        _response = await RegisterAsync(fullName, email, document);
    }

    [When("uma solicitação de cadastro chega sem {string}")]
    public async Task WhenRegistrationArrivesWithout(string information)
    {
        _customersBeforeRegistration = await CountCustomersAsync();
        _registeredEmail = "maria@ntix.com";
        _response = await RegisterAsync("Maria da Silva", _registeredEmail, "52998224725", AUDITABLE_HEADERS_BY_INFORMATION[information]);
    }

    [Then("meu cadastro é criado")]
    public async Task ThenCustomerIsCreated()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(_customersBeforeRegistration + 1, await CountCustomersAsync());
    }

    [Then("meu cadastro não é criado")]
    public async Task ThenCustomerIsNotCreated()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Equal(_customersBeforeRegistration, await CountCustomersAsync());
    }

    [Then("sou informado de que {string}")]
    public async Task ThenInformedThat(string message)
    {
        var notifications = await _response!.Content.ReadFromJsonAsync<NotificationResponse[]>();

        Assert.Contains(notifications!, notification => notification.Message == message);
    }

    [Then("recebo o slug {string}")]
    public async Task ThenReceivesSlug(string slug)
        => Assert.Equal(slug, await GetRegisteredSlugAsync());

    [Then("recebo o slug {string} seguido de uma versão de cinco letras ou números")]
    public async Task ThenReceivesVersionedSlug(string baseSlug)
        => Assert.Matches($"^{baseSlug}-[a-z0-9]{{5}}$", await GetRegisteredSlugAsync());
}
