using Application.UseCases.Base.Interfaces;
using Application.UseCases.ConfirmPaymentFromWebhook;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

/// <summary>
/// Webhooks dos fornecedores de pagamento (REQ8). Anônimo: quem chama é o AbacatePay, e a autenticidade vem da assinatura e do segredo.
/// As respostas não têm corpo: o detalhe fica no log e nas métricas, nunca para quem chama.
/// </summary>
[Route("api/v1/payment-webhooks")]
[ApiController]
public sealed class PaymentWebhooksV1Controller : ControllerBase
{
    private const int MAX_BODY_BYTES = 64 * 1024;
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    public const string WEBHOOK_SIGNATURE_HEADER_KEY = "X-Webhook-Signature";
    public const string WEBHOOK_SECRET_HEADER_KEY = "X-Webhook-Secret";

    // O AbacatePay não envia os headers auditáveis: o CorrelationId é gerado aqui (exceção registrada em standards.md).
    private const string ORIGIN_SERVICE = "abacatepay-webhook";
    private const string EXECUTION_USER = "abacatepay";

    /// <summary>Recebe o webhook do AbacatePay. 200 confirma ou registra o que não muda nada; 401, 404 e 400 não são reenviados; 503 e 500 são.</summary>
    [HttpPost("abacatepay")]
    public async Task<IActionResult> AbacatePayAsync(
        [FromServices] IUseCase<ConfirmPaymentFromWebhookUseCaseInput> useCase,
        [FromHeader(Name = WEBHOOK_SIGNATURE_HEADER_KEY)] string? signature,
        [FromHeader(Name = WEBHOOK_SECRET_HEADER_KEY)] string? secret,
        CancellationToken cancellationToken = default)
    {
        var body = await ReadBodyAsync(cancellationToken);

        if (body is null)
            return EmptyStatus(StatusCodes.Status400BadRequest);

        var auditableInfo = AuditableInfoValueObject.Build(
            correlationId: Guid.NewGuid().ToString(),
            originService: ORIGIN_SERVICE,
            executionUser: EXECUTION_USER,
            requestedAt: DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));

        var result = await useCase.ExecuteAsync(ConfirmPaymentFromWebhookUseCaseInput.Create(body, signature, secret), auditableInfo, cancellationToken);

        if (!result.IsError)
            return Ok();

        return EmptyStatus(MapStatusCode(result));
    }

    // Lê o corpo bruto até o limite: acima de 64 KB não é nem lido por inteiro (RN1, RNF de tamanho).
    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > MAX_BODY_BYTES)
            return null;

        var buffer = new byte[MAX_BODY_BYTES + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(total), cancellationToken);

            if (read == 0)
                break;

            total += read;
        }

        return total > MAX_BODY_BYTES ? null : buffer[..total];
    }

    private static int MapStatusCode(MethodResult result)
    {
        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(ConfirmPaymentFromWebhookUseCase.CONFIRM_PAYMENT_CANCELED_NOTIFICATION_CODE))
            return CLIENT_CLOSED_REQUEST_STATUS_CODE;

        if (codes.Overlaps([ConfirmPaymentFromWebhookUseCase.WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE, ConfirmPaymentFromWebhookUseCase.WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE]))
            return StatusCodes.Status401Unauthorized;

        if (codes.Contains(ConfirmPaymentFromWebhookUseCase.WEBHOOK_BODY_INVALID_NOTIFICATION_CODE))
            return StatusCodes.Status400BadRequest;

        if (codes.Contains(ConfirmPaymentFromWebhookUseCase.PAYMENT_NOT_FOUND_NOTIFICATION_CODE))
            return StatusCodes.Status404NotFound;

        if (codes.Contains(ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE))
            return StatusCodes.Status503ServiceUnavailable;

        return StatusCodes.Status500InternalServerError;
    }

    private EmptyResult EmptyStatus(int statusCode)
    {
        Response.StatusCode = statusCode;

        return new EmptyResult();
    }
}
