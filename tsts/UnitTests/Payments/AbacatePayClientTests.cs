using Infra.Http.Payments;
using Infra.Http.Payments.AbacatePay;
using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace UnitTests.Payments;

/// <summary>Tradução da resposta do AbacatePay (REQ7, RN7 e RN8), com um <see cref="HttpMessageHandler"/> falso: nada chama a rede.</summary>
public sealed class AbacatePayClientTests
{
    private static readonly PixChargeRequestModel REQUEST = new(
        PaymentId: Guid.Parse("5e9b1a2c-3d4e-4f5a-8b6c-7d8e9f0a1b2c"),
        OrderCode: "NX7K2QH9MP",
        AmountCents: 39000,
        ExpiresInSeconds: 780,
        Description: "Pedido NX7K2QH9MP - Festival Horizonte",
        CustomerName: "Marina Souza",
        CustomerEmail: "marina@exemplo.com",
        CustomerTaxId: "52998224725",
        CustomerCellphone: "(81) 99999-0000");

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return await respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string CREATED_BODY = """{"data":{"id":"pix_char_abc123","brCode":"00020101021226950014br.gov.bcb.pix","brCodeBase64":"data:image/png;base64,iVBORw0KGgo=","status":"PENDING"},"success":true,"error":null}""";

    private static (AbacatePayClient Client, StubHandler Handler) Build(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 5)
    {
        var handler = new StubHandler(respond);
        var configuration = new AbacatePayConfiguration { TimeoutSeconds = timeoutSeconds };
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.abacatepay.com/v2/"), Timeout = Timeout.InfiniteTimeSpan };

        return (new AbacatePayClient(httpClient, configuration), handler);
    }

    [Fact]
    public async Task Given_ProviderCreatesThePix_When_Charging_Should_ReturnReferenceCodeAndImage()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, CREATED_BODY)));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(PixChargeOutcome.Created, result.Outcome);
        Assert.Equal("pix_char_abc123", result.Reference);
        Assert.Equal("00020101021226950014br.gov.bcb.pix", result.CopyPasteCode);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", result.QrCodeImage);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task Given_Charge_When_Sending_Should_PostTheDocumentedBodyWithTheProducerKeyOnlyInTheHeader()
    {
        // Arrange
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, CREATED_BODY)));

        // Act
        await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.abacatepay.com/v2/transparents/create", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("abc_dev_chave", handler.Request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("abc_dev_chave", handler.Body);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.Equal("PIX", root.GetProperty("method").GetString());
        Assert.Equal(39000, data.GetProperty("amount").GetInt64());
        Assert.Equal(780, data.GetProperty("expiresIn").GetInt32());
        Assert.Equal("5e9b1a2c-3d4e-4f5a-8b6c-7d8e9f0a1b2c", data.GetProperty("externalId").GetString());
        Assert.Equal("Pedido NX7K2QH9MP - Festival Horizonte", data.GetProperty("description").GetString());
        Assert.Equal("52998224725", data.GetProperty("customer").GetProperty("taxId").GetString());
        Assert.Equal("(81) 99999-0000", data.GetProperty("customer").GetProperty("cellphone").GetString());
        Assert.Equal("NX7K2QH9MP", data.GetProperty("metadata").GetProperty("orderCode").GetString());
        Assert.Equal("5e9b1a2c-3d4e-4f5a-8b6c-7d8e9f0a1b2c", data.GetProperty("metadata").GetProperty("paymentId").GetString());
        Assert.False(data.GetProperty("metadata").TryGetProperty("taxId", out _));
    }

    [Fact]
    public async Task Given_CustomerWithoutCellphone_When_Sending_Should_SendAnEmptyStringBecauseTheProviderRequiresTheField()
    {
        // Arrange: no sandbox, ausente ou nulo dão 422 ("Property 'data.customer.cellphone' is missing"); vazio é aceito.
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, CREATED_BODY)));

        // Act
        await client.CreatePixChargeAsync("abc_dev_chave", REQUEST with { CustomerCellphone = string.Empty });

        // Assert
        using var document = JsonDocument.Parse(handler.Body!);

        Assert.Equal(string.Empty, document.RootElement.GetProperty("data").GetProperty("customer").GetProperty("cellphone").GetString());
    }

    [Fact]
    public async Task Given_ProviderRefusesWithItsReason_When_Charging_Should_KeepOnlyTheErrorTextAsDetail()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json((HttpStatusCode)422, """{"success":false,"data":null,"error":"Property 'data.customer.cellphone' is missing"}""")));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal("http_4xx", result.FailureReason);
        Assert.Equal("Property 'data.customer.cellphone' is missing", result.FailureDetail);
    }

    [Fact]
    public async Task Given_ProviderRefusesWithAVeryLongReason_When_Charging_Should_TruncateTheDetail()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, $$"""{"success":false,"error":"{{new string('x', 500)}}"}""")));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(200, result.FailureDetail!.Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Given_RefusalThatIsNotAValidationError_When_Charging_Should_NotCarryTheProviderText(HttpStatusCode status)
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(status, """{"success":false,"error":"texto do fornecedor"}""")));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Null(result.FailureDetail);
    }

    [Fact]
    public async Task Given_ValidationRefusalWithABodyThatIsNotJson_When_Charging_Should_ReportTheStatusWithoutDetail()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, "<html>Bad Request</html>")));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal("http_4xx", result.FailureReason);
        Assert.Null(result.FailureDetail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "http_401")]
    [InlineData(HttpStatusCode.TooManyRequests, "http_429")]
    [InlineData(HttpStatusCode.InternalServerError, "http_5xx")]
    [InlineData(HttpStatusCode.BadGateway, "http_5xx")]
    [InlineData(HttpStatusCode.BadRequest, "http_4xx")]
    [InlineData(HttpStatusCode.Forbidden, "http_4xx")]
    public async Task Given_ProviderRefuses_When_Charging_Should_ReportTheReasonByStatus(HttpStatusCode status, string reason)
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(status, """{"success":false,"error":"qualquer coisa que não deve vazar"}""")));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(PixChargeOutcome.ProviderError, result.Outcome);
        Assert.Equal(reason, result.FailureReason);
        Assert.Null(result.Reference);
    }

    [Theory]
    [InlineData("""{"success":false,"data":null,"error":"PIX is not available"}""")]
    [InlineData("""{"success":true,"data":{"id":"pix_char_1","brCode":"","brCodeBase64":"x"}}""")]
    [InlineData("""{"success":true,"data":{"brCode":"x","brCodeBase64":"x"}}""")]
    [InlineData("""{"success":true,"data":{"id":"pix_char_1","brCode":"x"}}""")]
    [InlineData("""{"success":true}""")]
    [InlineData("não é json")]
    [InlineData("null")]
    public async Task Given_OkWithoutAPix_When_Charging_Should_ReportInvalidResponse(string body)
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(PixChargeOutcome.ProviderError, result.Outcome);
        Assert.Equal("invalid_response", result.FailureReason);
    }

    [Fact]
    public async Task Given_ProviderThatNeverAnswers_When_TimeoutPasses_Should_ReportTimeoutWithoutThrowing()
    {
        // Arrange: tempo limite zero cancela a tentativa na hora.
        var (client, _) = Build(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);

            return Json(HttpStatusCode.OK, CREATED_BODY);
        }, timeoutSeconds: 0);

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(PixChargeOutcome.Unavailable, result.Outcome);
        Assert.Equal("timeout", result.FailureReason);
    }

    [Fact]
    public async Task Given_CallerCancels_When_Charging_Should_PropagateTheCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var (client, _) = Build(async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);

            return Json(HttpStatusCode.OK, CREATED_BODY);
        });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreatePixChargeAsync("abc_dev_chave", REQUEST, cancellation.Token));
    }

    [Fact]
    public async Task Given_NetworkFailure_When_Charging_Should_ReportNetworkWithoutTheMessage()
    {
        // Arrange
        var (client, _) = Build((_, _) => throw new HttpRequestException("falha com a chave abc_dev_chave na URL"));

        // Act
        var result = await client.CreatePixChargeAsync("abc_dev_chave", REQUEST);

        // Assert
        Assert.Equal(PixChargeOutcome.Unavailable, result.Outcome);
        Assert.Equal("network", result.FailureReason);
        Assert.DoesNotContain("abc_dev_chave", result.FailureReason);
    }

    [Fact]
    public void Given_Configuration_When_AddingTheClients_Should_ResolveTheAbacatePayAdapterForTheProviderCode()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AbacatePayConfiguration:BaseUrl"] = "https://sandbox.exemplo.com/v2/", ["AbacatePayConfiguration:TimeoutSeconds"] = "7" })
            .Build();
        using var provider = new ServiceCollection().AddPixChargeClientsConfiguration(configuration).BuildServiceProvider();

        // Act
        var clients = provider.GetServices<IPixChargeClient>().ToArray();

        // Assert
        var client = Assert.Single(clients);

        Assert.IsType<AbacatePayClient>(client);
        Assert.Equal(AbacatePayClient.PROVIDER_CODE, client.ProviderCode);
        Assert.Equal(7, provider.GetRequiredService<AbacatePayConfiguration>().TimeoutSeconds);
    }

    [Fact]
    public void Given_NoConfiguration_When_AddingTheClients_Should_UseTheDocumentedDefaults()
    {
        // Arrange
        using var provider = new ServiceCollection().AddPixChargeClientsConfiguration(new ConfigurationBuilder().Build()).BuildServiceProvider();

        // Act
        var configuration = provider.GetRequiredService<AbacatePayConfiguration>();

        // Assert
        Assert.Equal("https://api.abacatepay.com/v2", configuration.BaseUrl);
        Assert.Equal(5, configuration.TimeoutSeconds);
    }
}
