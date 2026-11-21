using Domain.CrossCutting.MethodResultContext;

namespace Application.UseCases.ConfirmPaymentFromWebhook.Inputs;

/// <summary>
/// Webhook recebido do AbacatePay (REQ8). O corpo bruto vai como bytes, porque a assinatura é calculada sobre eles (RN1).
/// O segredo vem do header <c>X-Webhook-Secret</c>; o da URL nunca é usado, porque URL vai para log.
/// </summary>
public sealed record ConfirmPaymentFromWebhookUseCaseInput
{
    private ConfirmPaymentFromWebhookUseCaseInput(byte[] rawBody, string? signature, string? secret)
    {
        RawBody = rawBody;
        Signature = signature;
        Secret = secret;
    }

    public byte[] RawBody { get; }

    public string? Signature { get; }

    public string? Secret { get; }

    public static ConfirmPaymentFromWebhookUseCaseInput Create(byte[] rawBody, string? signature, string? secret)
        => new(rawBody, signature, secret);

    // A validação é a da assinatura e do corpo, feitas no caso de uso, antes de qualquer consulta ao banco.
    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
