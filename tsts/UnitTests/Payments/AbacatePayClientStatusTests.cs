using Infra.Http.Payments.AbacatePay;
using System.Net;
using System.Text;

namespace UnitTests.Payments;

/// <summary>Consulta da situação na fonte (REQ8, RN4), com um <see cref="HttpMessageHandler"/> falso: nada chama a rede.</summary>
public sealed class AbacatePayClientStatusTests
{
    private const string PROVIDER_REFERENCE = "pix_char_abc123";
    private const string API_KEY = "abc_dev_chave-de-teste";

    private const string PAID_BODY = """{"data":{"id":"pix_char_abc123","status":"PAID","expiresAt":"2026-11-14T12:15:00.000Z"},"error":null,"success":true}""";

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AbacatePayClient Client, StubHandler Handler) Build(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 5)
    {
        var handler = new StubHandler(respond);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.abacatepay.com/v2/"), Timeout = Timeout.InfiniteTimeSpan };

        return (new AbacatePayClient(httpClient, new AbacatePayConfiguration { TimeoutSeconds = timeoutSeconds }), handler);
    }

    [Fact]
    public async Task Given_PaidCharge_When_QueryingStatus_Should_ReturnPaid()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, PAID_BODY)));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.True(result.Available);
        Assert.Equal("PAID", result.Status);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task Given_ValidRequest_When_QueryingStatus_Should_CallTheCheckRouteWithBearerKeyAndEscapedId()
    {
        // Arrange
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, PAID_BODY)));

        // Act
        await client.GetPixChargeStatusAsync(API_KEY, "pix char/1");

        // Assert
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Contains("transparents/check?id=pix%20char%2F1", handler.Request.RequestUri!.OriginalString, StringComparison.Ordinal);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(API_KEY, handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Given_ProviderServerError_When_QueryingStatus_Should_ReturnUnavailableWithHttp5xxReason()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{}")));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("http_5xx", result.FailureReason);
    }

    [Fact]
    public async Task Given_ProviderRejectsTheKey_When_QueryingStatus_Should_ReturnUnavailableWithHttp401Reason()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, """{"error":"Invalid key"}""")));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("http_401", result.FailureReason);
    }

    [Fact]
    public async Task Given_ProviderSaysNotSuccessful_When_QueryingStatus_Should_ReturnInvalidResponse()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"data":{"status":"PAID"},"error":"x","success":false}""")));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("invalid_response", result.FailureReason);
    }

    [Fact]
    public async Task Given_ResponseWithoutStatus_When_QueryingStatus_Should_ReturnInvalidResponse()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"data":{"id":"pix_char_abc123"},"success":true}""")));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("invalid_response", result.FailureReason);
    }

    [Fact]
    public async Task Given_MalformedJson_When_QueryingStatus_Should_ReturnInvalidResponse()
    {
        // Arrange
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "isto não é json")));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("invalid_response", result.FailureReason);
    }

    [Fact]
    public async Task Given_TimeoutExceeded_When_QueryingStatus_Should_ReturnUnavailableWithTimeout()
    {
        // Arrange: o tempo limite de zero segundos cancela a tentativa antes da resposta.
        var (client, _) = Build(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return Json(HttpStatusCode.OK, PAID_BODY);
        }, timeoutSeconds: 0);

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("timeout", result.FailureReason);
    }

    [Fact]
    public async Task Given_NetworkFailure_When_QueryingStatus_Should_ReturnUnavailableWithNetworkReason()
    {
        // Arrange
        var (client, _) = Build((_, _) => throw new HttpRequestException("conexão recusada"));

        // Act
        var result = await client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE);

        // Assert
        Assert.False(result.Available);
        Assert.Equal("network", result.FailureReason);
    }

    [Fact]
    public async Task Given_CallerCancellation_When_QueryingStatus_Should_PropagateTheCancellation()
    {
        // Arrange (RN7): o cancelamento de quem chama não vira "indisponível"; propaga.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var (client, _) = Build(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return Json(HttpStatusCode.OK, PAID_BODY);
        });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPixChargeStatusAsync(API_KEY, PROVIDER_REFERENCE, cancellation.Token));
    }
}
