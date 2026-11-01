using Domain.ValueObjects;
using FunctionalTests.Support;
using Microsoft.Data.SqlClient;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class ProducerRegistrationSteps
{
    private const string PRODUCERS_ROUTE = "api/v1/producers";
    private const string COUNT_PRODUCERS_SQL = "SELECT COUNT(*) FROM producers.Producer;";
    private const string COUNT_PENDING_PRODUCERS_SQL = "SELECT COUNT(*) FROM producers.Producer WHERE email = @email AND status = 'PENDING';";
    private const string COUNT_PENDING_ADMINISTRATORS_SQL = "SELECT COUNT(*) FROM producers.ProducerUser WHERE email = @email AND user_type = 'ADMINISTRATOR' AND status = 'PENDING';";
    private const string SELECT_SLUG_SQL = "SELECT slug FROM producers.Producer WHERE email = @email;";
    private const string SELECT_PHONE_SQL = "SELECT phone FROM producers.Producer WHERE email = @email;";

    private const string SELECT_DOCUMENT_SQL = "SELECT document FROM producers.Producer WHERE email = @email;";

    private const string NAME = "Ação Eventos";
    private const string EMAIL = "contato@acao.com";
    private const string PHONE = "+5511999999999";
    private const string USER_NAME = "Maria da Silva";
    private const string PASSWORD = "Senha@123";

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private HttpResponseMessage? _response;
    private string? _email;
    private int _producersBeforeRegistration;

    private sealed record NotificationResponse(string Code, string Message, string Type);

    private Task<HttpResponseMessage> RegisterAsync(string name = NAME, string email = EMAIL, string phone = PHONE, string password = PASSWORD, bool withExecutionUser = true, string? document = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PRODUCERS_ROUTE)
        {
            Content = JsonContent.Create(new { name, email, phone, document, userName = USER_NAME, password })
        };

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        if (withExecutionUser)
            request.Headers.Add(AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY, "user-123");

        return _client.SendAsync(request);
    }

    private static Task<int> CountProducersAsync()
        => NtixApiHooks.Database.ScalarAsync<int>(COUNT_PRODUCERS_SQL);

    private async Task SubmitAsync(string name = NAME, string email = EMAIL, string phone = PHONE, string password = PASSWORD, bool withExecutionUser = true, string? document = null)
    {
        _producersBeforeRegistration = await CountProducersAsync();
        _email = email;
        _response = await RegisterAsync(name, email, phone, password, withExecutionUser, document);
    }

    private Task<string> GetSlugAsync()
        => NtixApiHooks.Database.ScalarAsync<string>(SELECT_SLUG_SQL, new SqlParameter("@email", _email));

    [Given("que já existe uma produtora {string} com o email {string}")]
    public async Task GivenExistingProducer(string name, string email)
    {
        var response = await RegisterAsync(name, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Given("que o envio de emails para {string} está indisponível")]
    public void GivenEmailSendingUnavailable(string email)
        => NtixApiHooks.EmailSender.FailFor(email);

    [Given("que já existe uma produtora com o documento {string}")]
    public async Task GivenExistingProducerWithDocument(string document)
    {
        var response = await RegisterAsync(name: "Produtora Existente", email: "existente@acao.com", document: document);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [When("cadastro uma produtora com o documento {string}")]
    public Task WhenRegisteringWithDocument(string document)
        => SubmitAsync(document: document);

    [Then("o documento {string} fica registrado")]
    public async Task ThenDocumentIsRegistered(string document)
        => Assert.Equal(document, await NtixApiHooks.Database.ScalarAsync<string>(SELECT_DOCUMENT_SQL, new SqlParameter("@email", _email)));

    [When("cadastro a produtora {string} com o email {string}")]
    public Task WhenRegistering(string name, string email)
        => SubmitAsync(name: name, email: email);

    [When("cadastro uma produtora com o telefone {string}")]
    public Task WhenRegisteringWithPhone(string phone)
        => SubmitAsync(phone: phone);

    [When("cadastro uma produtora com a senha do administrador {string}")]
    public Task WhenRegisteringWithPassword(string password)
        => SubmitAsync(password: password);

    [When("cadastro uma produtora sem informar o usuário executor")]
    public Task WhenRegisteringWithoutExecutionUser()
        => SubmitAsync(withExecutionUser: false);

    [Then("a produtora é criada aguardando o envio do cadastro para aprovação")]
    public async Task ThenProducerIsCreatedPending()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);
        Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PENDING_PRODUCERS_SQL, new SqlParameter("@email", _email)));
    }

    [Then("o usuário administrador é criado aguardando a verificação do email")]
    public async Task ThenAdministratorIsCreatedPending()
        => Assert.Equal(1, await NtixApiHooks.Database.ScalarAsync<int>(COUNT_PENDING_ADMINISTRATORS_SQL, new SqlParameter("@email", _email)));

    [Then("a produtora não é criada")]
    public async Task ThenProducerIsNotCreated()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);
        Assert.Equal(_producersBeforeRegistration, await CountProducersAsync());
    }

    [Then("a produtora recebe o endereço {string}")]
    public async Task ThenReceivesSlug(string slug)
        => Assert.Equal(slug, await GetSlugAsync());

    [Then("a produtora recebe o endereço {string} seguido de uma versão de cinco letras ou números")]
    public async Task ThenReceivesVersionedSlug(string baseSlug)
        => Assert.Matches($"^{baseSlug}-[a-z0-9]{{5}}$", await GetSlugAsync());

    [Then("um link de verificação é enviado para {string}")]
    public void ThenVerificationLinkIsSent(string email)
    {
        var sentEmail = Assert.Single(NtixApiHooks.EmailSender.Sent);

        Assert.Equal(email, sentEmail.To);
        Assert.Matches("verificar-email\\?token=[a-f0-9]{64}", sentEmail.Html);
    }

    [Then("nenhum link de verificação é enviado")]
    public void ThenNoVerificationLinkIsSent()
        => Assert.Empty(NtixApiHooks.EmailSender.Sent);

    [Then("o telefone {string} fica registrado")]
    public async Task ThenPhoneIsRegistered(string phone)
        => Assert.Equal(phone, await NtixApiHooks.Database.ScalarAsync<string>(SELECT_PHONE_SQL, new SqlParameter("@email", _email)));

    [Then("recebo o aviso {string}")]
    public async Task ThenReceivesNotice(string message)
    {
        var notifications = await _response!.Content.ReadFromJsonAsync<NotificationResponse[]>();

        Assert.Contains(notifications!, notification => notification.Message == message);
    }
}
