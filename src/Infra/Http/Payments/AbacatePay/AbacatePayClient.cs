using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infra.Http.Payments.AbacatePay;

/// <summary>
/// Pix pelo AbacatePay (<c>POST /v2/transparents/create</c>), com a chave do produtor. A chave só vai no header da requisição
/// e nunca em log, span ou exceção; o span leva o código do fornecedor e o status HTTP, jamais corpo nem headers (REQ7, RN10).
/// </summary>
public sealed class AbacatePayClient : IPixChargeClient
{
    public const string PROVIDER_CODE = "ABACATEPAY";

    private const string CREATE_ROUTE = "transparents/create";
    private const int DETAIL_MAX_LENGTH = 200;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.Http");

    private static readonly JsonSerializerOptions JSON_OPTIONS = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _httpClient;
    private readonly AbacatePayConfiguration _configuration;

    public AbacatePayClient(HttpClient httpClient, AbacatePayConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public string ProviderCode => PROVIDER_CODE;

    public async Task<PixChargeResultModel> CreatePixChargeAsync(string apiKey, PixChargeRequestModel request, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(AbacatePayClient)}.{nameof(CreatePixChargeAsync)}", ActivityKind.Client)
            ?.SetTag("ntix.payments.provider", PROVIDER_CODE);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(_configuration.TimeoutSeconds));

        using var message = new HttpRequestMessage(HttpMethod.Post, CREATE_ROUTE) { Content = JsonContent.Create(BuildBody(request), options: JSON_OPTIONS) };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(message, timeout.Token);

            activity?.SetTag("http.response.status_code", (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return await ReadRefusalAsync(response, timeout.Token);

            return await ReadChargeAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "timeout");

            return PixChargeResultModel.Unavailable("timeout");
        }
        catch (HttpRequestException ex)
        {
            // O tipo da exceção basta: a mensagem pode trazer a URL ou detalhes da conexão.
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            return PixChargeResultModel.Unavailable("network");
        }
    }

    private static async Task<PixChargeResultModel> ReadChargeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ChargeEnvelope>(JSON_OPTIONS, cancellationToken);
            var data = envelope?.Data;

            if (envelope?.Success == false || string.IsNullOrWhiteSpace(data?.Id) || string.IsNullOrWhiteSpace(data.BrCode) || string.IsNullOrWhiteSpace(data.BrCodeBase64))
                return PixChargeResultModel.ProviderError("invalid_response");

            return PixChargeResultModel.Created(data.Id, data.BrCode, data.BrCodeBase64);
        }
        catch (JsonException)
        {
            return PixChargeResultModel.ProviderError("invalid_response");
        }
    }

    /// <summary>
    /// Recusa do fornecedor. Na 4xx (422, 400...), o motivo vem no campo <c>error</c> da resposta (ex.: "Property 'data.customer.cellphone' is missing"):
    /// só esse texto, truncado, vai para o log, para quem opera descobrir o que ajustar sem ler o código. Nunca o corpo inteiro nem os headers.
    /// </summary>
    private static async Task<PixChargeResultModel> ReadRefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var reason = GetStatusReason(status);

        if (status is < 400 or >= 500 or 401 or 429)
            return PixChargeResultModel.ProviderError(reason);

        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ChargeEnvelope>(JSON_OPTIONS, cancellationToken);
            var detail = envelope?.Error;

            return PixChargeResultModel.ProviderError(reason, detail is { Length: > DETAIL_MAX_LENGTH } ? detail[..DETAIL_MAX_LENGTH] : detail);
        }
        catch (JsonException)
        {
            return PixChargeResultModel.ProviderError(reason);
        }
    }

    private static string GetStatusReason(int status)
        => status switch
        {
            401 => "http_401",
            429 => "http_429",
            >= 500 => "http_5xx",
            _ => "http_4xx",
        };

    // RN8: valor em centavos, validade em segundos e o id do pagamento como externalId.
    private static object BuildBody(PixChargeRequestModel request)
        => new
        {
            method = "PIX",
            data = new
            {
                amount = request.AmountCents,
                expiresIn = request.ExpiresInSeconds,
                externalId = request.PaymentId.ToString(),
                description = request.Description,
                customer = new
                {
                    name = request.CustomerName,
                    email = request.CustomerEmail,
                    taxId = request.CustomerTaxId,
                    cellphone = request.CustomerCellphone,
                },
                metadata = new { orderCode = request.OrderCode, paymentId = request.PaymentId.ToString() },
            },
        };

    private sealed record ChargeEnvelope(bool? Success, ChargeData? Data, string? Error = null);

    private sealed record ChargeData(string? Id, string? BrCode, string? BrCodeBase64);
}
