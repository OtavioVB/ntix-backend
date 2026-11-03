using Domain.ValueObjects;
using FunctionalTests.Support;
using Npgsql;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class CustomerRegistrationSteps
{
    private const string CUSTOMERS_ROUTE = "api/v1/customers";
    private const string COUNT_CUSTOMERS_SQL = "SELECT COUNT(*)::int FROM customers.customer;";
    private const string SELECT_SLUG_BY_EMAIL_SQL = "SELECT slug FROM customers.customer WHERE email = @email;";
    private const string SELECT_PASSWORD_HASH_BY_EMAIL_SQL = "SELECT password_hash FROM customers.customer WHERE email = @email;";
    private const string COUNT_CUSTOMERS_BY_EMAIL_AND_PHONE_NUMBER_SQL = "SELECT COUNT(*)::int FROM customers.customer WHERE email = @email AND phone_number = @phone_number;";
    private const string COUNT_CUSTOMERS_BY_EMAIL_WITHOUT_PHONE_NUMBER_SQL = "SELECT COUNT(*)::int FROM customers.customer WHERE email = @email AND phone_number IS NULL;";

    private const string FULL_NAME = "Maria da Silva";
    private const string EMAIL = "maria@ntix.com";
    private const string DOCUMENT = "52998224725";
    private const string PASSWORD = "Senha@123";

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
    private string? _registeredPassword;
    private int _customersBeforeRegistration;

    private sealed record NotificationResponse(string Code, string Message, string Type);

    private Task<HttpResponseMessage> RegisterAsync(string fullName, string email, string document, string? omittedHeader = null, string password = PASSWORD, string? phoneNumber = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, CUSTOMERS_ROUTE)
        {
            Content = JsonContent.Create(new { fullName, email, document, password, phoneNumber })
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
            new NpgsqlParameter("@email", _registeredEmail!.Trim().ToLowerInvariant()));

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
        _registeredEmail = EMAIL;
        _response = await RegisterAsync(FULL_NAME, _registeredEmail, DOCUMENT, AUDITABLE_HEADERS_BY_INFORMATION[information]);
    }

    [When("me cadastro com a senha {string}")]
    public async Task WhenRegisteringWithPassword(string password)
    {
        _customersBeforeRegistration = await CountCustomersAsync();
        _registeredEmail = EMAIL;
        _registeredPassword = password;
        _response = await RegisterAsync(FULL_NAME, EMAIL, DOCUMENT, password: password);
    }

    [When("me cadastro com o celular {string}")]
    public async Task WhenRegisteringWithPhoneNumber(string phoneNumber)
    {
        _customersBeforeRegistration = await CountCustomersAsync();
        _registeredEmail = EMAIL;
        _response = await RegisterAsync(FULL_NAME, EMAIL, DOCUMENT, phoneNumber: phoneNumber);
    }

    [When("me cadastro sem informar o celular")]
    public async Task WhenRegisteringWithoutPhoneNumber()
    {
        _customersBeforeRegistration = await CountCustomersAsync();
        _registeredEmail = EMAIL;
        _response = await RegisterAsync(FULL_NAME, EMAIL, DOCUMENT);
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

    [Then("minha senha não fica guardada como foi escrita")]
    public async Task ThenPasswordIsNotStoredAsWritten()
    {
        var passwordHash = await NtixApiHooks.Database.ScalarAsync<byte[]>(
            SELECT_PASSWORD_HASH_BY_EMAIL_SQL,
            new NpgsqlParameter("@email", _registeredEmail));

        Assert.NotEqual(Encoding.UTF8.GetBytes(_registeredPassword!), passwordHash);
        Assert.DoesNotContain(_registeredPassword!, Encoding.UTF8.GetString(passwordHash));
    }

    [Then("o celular {string} fica registrado")]
    public async Task ThenPhoneNumberIsRegistered(string phoneNumber)
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(
            COUNT_CUSTOMERS_BY_EMAIL_AND_PHONE_NUMBER_SQL,
            new NpgsqlParameter("@email", _registeredEmail),
            new NpgsqlParameter("@phone_number", phoneNumber)));

    [Then("nenhum celular fica registrado")]
    public async Task ThenNoPhoneNumberIsRegistered()
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(
            COUNT_CUSTOMERS_BY_EMAIL_WITHOUT_PHONE_NUMBER_SQL,
            new NpgsqlParameter("@email", _registeredEmail)));
}
