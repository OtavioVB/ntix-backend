using Domain.ValueObjects;
using FunctionalTests.Support;
using Reqnroll;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FunctionalTests.StepDefinitions;

[Binding]
public sealed class ProducerWhitelabelSteps
{
    private const string WHITELABEL_ROUTE = "api/v1/producers/{0}/whitelabel";
    private const int MAX_FILE_BYTES = 64 * 1024;

    private readonly HttpClient _client = NtixApiHooks.Factory.CreateClient();

    private HttpResponseMessage? _response;
    private string? _internalNote;

    private sealed record NotificationResponse(string Code, string Message, string Type);

    private static string FilePath(string slug)
        => Path.Combine(NtixApiHooks.WhitelabelDirectory, slug + ".json");

    private static JsonObject ReadFile(string slug)
        => JsonNode.Parse(File.ReadAllText(FilePath(slug)))!.AsObject();

    private static void WriteFile(string slug, JsonObject content)
        => File.WriteAllText(FilePath(slug), content.ToJsonString());

    [Given("que a Ntix configurou a identidade visual da produtora {string} com a cor principal {string} e o nome {string}")]
    public void GivenConfiguredWhitelabel(string slug, string primaryColor, string name)
        => WriteFile(slug, new JsonObject
        {
            ["version"] = 1,
            ["brand"] = new JsonObject { ["name"] = name, ["faviconUrl"] = "/favicon.svg" },
            ["colors"] = new JsonObject { ["primary"] = new JsonObject { ["500"] = primaryColor } },
            ["radius"] = new JsonObject { ["md"] = "8px" },
        });

    [Given("que a configuração da produtora {string} contém a anotação interna {string}")]
    public void GivenInternalNote(string slug, string note)
    {
        _internalNote = note;

        var content = ReadFile(slug);
        content["internalNotes"] = note;
        content["brand"]!["contractNote"] = note;
        WriteFile(slug, content);
    }

    [Given("que a configuração da produtora {string} tem o logo {string}")]
    public void GivenLogo(string slug, string logoUrl)
    {
        var content = ReadFile(slug);
        content["brand"]!["logoUrl"] = logoUrl;
        WriteFile(slug, content);
    }

    [Given("que a configuração da produtora {string} passou do tamanho permitido")]
    public void GivenFileAboveLimit(string slug)
    {
        var content = ReadFile(slug);
        content["padding"] = new string('x', MAX_FILE_BYTES);
        WriteFile(slug, content);
    }

    [When("a plataforma consulta a identidade visual da produtora {string}")]
    public async Task WhenPlatformQueriesWhitelabel(string slug)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, string.Format(CultureInfo.InvariantCulture, WHITELABEL_ROUTE, slug));

        request.Headers.Add(AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY, Guid.NewGuid().ToString());
        request.Headers.Add(AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY, "functional-tests");
        request.Headers.Add(AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY, DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

        _response = await _client.SendAsync(request);
    }

    [Then("recebe a cor principal {string} e o nome {string}")]
    public async Task ThenReceivesWhitelabel(string primaryColor, string name)
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var body = JsonNode.Parse(await _response.Content.ReadAsStringAsync())!;

        Assert.Equal(primaryColor, body["colors"]!["primary"]!["500"]!.GetValue<string>());
        Assert.Equal(name, body["brand"]!["name"]!.GetValue<string>());
        Assert.Equal("8px", body["radius"]!["md"]!.GetValue<string>());
        // Campo ausente no arquivo fica ausente na resposta, para o front usar o padrão Ntix.
        Assert.Null(body["typography"]);
    }

    [Then("a identidade visual pode ser guardada em cache por 5 minutos")]
    public void ThenCacheableForFiveMinutes()
    {
        Assert.True(_response!.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), _response.Headers.CacheControl.MaxAge);
    }

    [Then("a anotação interna não aparece para quem consulta")]
    public async Task ThenInternalNoteIsHidden()
    {
        Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

        var body = await _response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(_internalNote!, body);
        Assert.DoesNotContain("internalNotes", body);
        Assert.DoesNotContain("contractNote", body);
    }

    [Then("é informado que não existe identidade visual para essa produtora")]
    public async Task ThenNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _response!.StatusCode);

        var notifications = await _response.Content.ReadFromJsonAsync<NotificationResponse[]>(JsonSerializerOptions.Web);

        Assert.Contains(notifications!, notification => notification.Code == "WHITELABEL_NOT_FOUND");
    }

    [Then("é informado que o endereço da produtora é inválido")]
    public async Task ThenSlugInvalid()
    {
        Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);

        var notifications = await _response.Content.ReadFromJsonAsync<NotificationResponse[]>(JsonSerializerOptions.Web);

        Assert.Contains(notifications!, notification => notification.Code == "SLUG_INVALID");
    }

    [Then("a identidade visual não é servida e nenhum detalhe do problema é revelado")]
    public async Task ThenNotServedWithoutDetail()
    {
        Assert.Equal(HttpStatusCode.InternalServerError, _response!.StatusCode);
        Assert.Empty(await _response.Content.ReadAsStringAsync());
    }
}
