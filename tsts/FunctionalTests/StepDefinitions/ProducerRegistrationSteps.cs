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
public sealed class ProducerRegistrationSteps
{
    private const string PRODUCERS_ROUTE = "api/v1/producers";

    private const string COUNT_PRODUCERS_SQL = "SELECT COUNT(*)::int FROM producers.producer;";
    private const string COUNT_PRODUCER_USERS_SQL = "SELECT COUNT(*)::int FROM producers.producer_user;";
    private const string SELECT_PRODUCER_COLUMN_BY_EMAIL_SQL = "SELECT {0} FROM producers.producer WHERE email = @email;";
    private const string COUNT_PENDING_ADMINISTRATOR_SQL = """
        SELECT COUNT(*)::int
        FROM producers.producer_user u
        JOIN producers.producer p ON p.id_producer = u.id_producer
        WHERE u.email = @email AND u.name = @name AND u.user_type = 'ADMINISTRATOR' AND u.status = 'PENDING';
        """;
    private const string SELECT_PASSWORD_HASH_BY_EMAIL_SQL = "SELECT password_hash FROM producers.producer_user WHERE email = @email;";
    private const string COUNT_PRODUCER_REGISTERED_EVENTS_SQL = """
        SELECT COUNT(*)::int
        FROM outbox.outbox_event e
        JOIN producers.producer p ON p.id_producer = e.aggregate_id
        WHERE p.email = @email AND e.event_type = 'ProducerRegistered';
        """;

    private const string NAME = "Ação Eventos & Shows";
    private const string EMAIL = "contato@acao.com";
    private const string PHONE = "(11) 98765-4321";
    private const string ADMINISTRATOR_NAME = "Maria da Silva";
    private const string PASSWORD = "Senha@123";

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private HttpResponseMessage? _response;
    private string? _registeredEmail;
    private string? _registeredPassword;
    private int _producersBefore;
    private int _producerUsersBefore;

    private sealed record NotificationResponse(string Code, string Message, string Type);

    private Task<HttpResponseMessage> RegisterAsync(string name = NAME, string email = EMAIL, string phone = PHONE, string administratorName = ADMINISTRATOR_NAME, string password = PASSWORD)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PRODUCERS_ROUTE)
        {
            Content = JsonContent.Create(new { name, email, phone, administratorName, administratorPassword = password })
        };

        // Cadastro público: sem X-Execution-User.
        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        return _client.SendAsync(request);
    }

    private async Task RegisterAndRememberAsync(string name = NAME, string email = EMAIL, string phone = PHONE, string administratorName = ADMINISTRATOR_NAME, string password = PASSWORD)
    {
        _producersBefore = await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PRODUCERS_SQL);
        _producerUsersBefore = await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PRODUCER_USERS_SQL);
        _registeredEmail = email.Trim().ToLowerInvariant();
        _registeredPassword = password;
        _response = await RegisterAsync(name, email, phone, administratorName, password);
    }

    private Task<string> GetProducerColumnAsync(string column)
        => NtixApiHooks.Database.ScalarAsync<string>(
            string.Format(CultureInfo.InvariantCulture, SELECT_PRODUCER_COLUMN_BY_EMAIL_SQL, column),
            new NpgsqlParameter("@email", _registeredEmail));

    [Given("que já existe a produtora {string} com o e-mail {string}")]
    public async Task GivenExistingProducer(string name, string email)
        => Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(name: name, email: email)).StatusCode);

    [When("cadastro a produtora {string} com o e-mail {string} e o administrador {string}")]
    public Task WhenRegisteringProducer(string name, string email, string administratorName)
        => RegisterAndRememberAsync(name: name, email: email, administratorName: administratorName);

    [When("cadastro a produtora com o telefone {string}")]
    public Task WhenRegisteringWithPhone(string phone)
        => RegisterAndRememberAsync(phone: phone);

    [When("cadastro a produtora com a senha de administrador {string}")]
    public Task WhenRegisteringWithPassword(string password)
        => RegisterAndRememberAsync(password: password);

    [When("a produtora é cadastrada por um visitante sem usuário identificado")]
    public Task WhenRegisteredByVisitor()
        => RegisterAndRememberAsync();

    [Then("a produtora é cadastrada aguardando o envio para aprovação")]
    public async Task ThenProducerIsPending()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal("PENDING", await GetProducerColumnAsync("status"));
    }

    [Then("o administrador {string} é criado aguardando a verificação do e-mail")]
    public async Task ThenAdministratorIsPending(string administratorName)
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(
            COUNT_PENDING_ADMINISTRATOR_SQL,
            new NpgsqlParameter("@email", _registeredEmail),
            new NpgsqlParameter("@name", administratorName)));

    [Then("a produtora recebe o endereço {string}")]
    public async Task ThenProducerReceivesSlug(string slug)
        => Assert.Equal(slug, await GetProducerColumnAsync("slug"));

    [Then("a produtora recebe o endereço {string} seguido de uma versão de cinco letras ou números")]
    public async Task ThenProducerReceivesVersionedSlug(string baseSlug)
        => Assert.Matches($"^{baseSlug}-[a-z0-9]{{5}}$", await GetProducerColumnAsync("slug"));

    [Then("o telefone {string} fica registrado na produtora")]
    public async Task ThenPhoneIsRegistered(string phone)
        => Assert.Equal(phone, await GetProducerColumnAsync("phone"));

    [Then("a criação da carteira interna da produtora fica registrada uma única vez")]
    public async Task ThenWalletCreationIsRecordedOnce()
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(
            COUNT_PRODUCER_REGISTERED_EVENTS_SQL,
            new NpgsqlParameter("@email", _registeredEmail)));

    [Then("nada é cadastrado")]
    public async Task ThenNothingIsRegistered()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Equal(_producersBefore, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PRODUCERS_SQL));
        Assert.Equal(_producerUsersBefore, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PRODUCER_USERS_SQL));
    }

    [Then("sou avisado de que {string}")]
    public async Task ThenWarnedThat(string message)
    {
        var notifications = await _response!.Content.ReadFromJsonAsync<NotificationResponse[]>();

        Assert.Contains(notifications!, notification => notification.Message == message);
    }

    [Then("a senha do administrador não fica guardada como foi escrita")]
    public async Task ThenPasswordIsNotStoredAsWritten()
    {
        var passwordHash = await NtixApiHooks.Database.ScalarAsync<byte[]>(
            SELECT_PASSWORD_HASH_BY_EMAIL_SQL,
            new NpgsqlParameter("@email", _registeredEmail));

        Assert.NotEqual(Encoding.UTF8.GetBytes(_registeredPassword!), passwordHash);
        Assert.DoesNotContain(_registeredPassword!, Encoding.UTF8.GetString(passwordHash));
    }
}
