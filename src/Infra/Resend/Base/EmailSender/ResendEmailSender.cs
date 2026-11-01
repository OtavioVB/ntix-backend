using Infra.Resend.Base.Configuration;
using Infra.Resend.Base.EmailSender.Interfaces;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Infra.Resend.Base.EmailSender;

public sealed class ResendEmailSender : IEmailSender
{
    private const string EMAILS_ROUTE = "emails";
    private const string BEARER_SCHEME = "Bearer";

    private readonly HttpClient _httpClient;
    private readonly ResendConfiguration _configuration;

    public ResendEmailSender(HttpClient httpClient, ResendConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EMAILS_ROUTE)
        {
            Content = JsonContent.Create(new { from = _configuration.From, to = new[] { to }, subject, html })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(BEARER_SCHEME, _configuration.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
