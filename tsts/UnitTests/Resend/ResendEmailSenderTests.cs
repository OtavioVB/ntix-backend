using Infra.Resend.Base.Configuration;
using Infra.Resend.Base.EmailSender;
using System.Net;
using System.Text.Json;

namespace UnitTests.Resend;

public sealed class ResendEmailSenderTests
{
    private const string API_KEY = "re_chave-de-teste";
    private const string FROM = "Ntix <nao-responda@ntix.com>";
    private const string TO = "maria@ntix.com";

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode);
        }
    }

    private static ResendEmailSender BuildSender(HttpMessageHandler handler)
        => new(
            httpClient: new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") },
            configuration: new ResendConfiguration(API_KEY, FROM));

    [Fact]
    public async Task Given_Email_When_Sending_Should_PostToResendWithApiKeyAndContent()
    {
        // Arrange
        var handler = new CapturingHandler(HttpStatusCode.OK);

        // Act
        await BuildSender(handler).SendAsync(TO, "Assunto", "<p>Corpo</p>");

        // Assert
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.resend.com/emails", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal(API_KEY, handler.Request.Headers.Authorization.Parameter);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(FROM, body.RootElement.GetProperty("from").GetString());
        Assert.Equal(TO, body.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("Assunto", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal("<p>Corpo</p>", body.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task Given_ResendRefusal_When_Sending_Should_Throw()
    {
        // Arrange
        var sender = BuildSender(new CapturingHandler(HttpStatusCode.Unauthorized));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(TO, "Assunto", "<p>Corpo</p>"));
    }
}
