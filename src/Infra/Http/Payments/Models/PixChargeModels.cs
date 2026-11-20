namespace Infra.Http.Payments.Models;

/// <summary>
/// Pedido de cobrança Pix a um fornecedor (REQ7, RN8). Dado pessoal só nos campos do cliente que o fornecedor exige;
/// <c>OrderCode</c> e <c>PaymentId</c> vão em <c>metadata</c>, sem nenhum outro dado pessoal.
/// </summary>
public sealed record PixChargeRequestModel(
    Guid PaymentId,
    string OrderCode,
    long AmountCents,
    int ExpiresInSeconds,
    string Description,
    string CustomerName,
    string CustomerEmail,
    string CustomerTaxId,
    string? CustomerCellphone);

public enum PixChargeOutcome
{
    /// <summary>O fornecedor gerou o Pix e devolveu o id, o copia e cola e a imagem.</summary>
    Created = 1,

    /// <summary>O fornecedor respondeu, mas recusou ou devolveu algo sem Pix. Vale a próxima integração (REQ7, RN7).</summary>
    ProviderError = 2,

    /// <summary>Sem resposta: tempo esgotado ou falha de rede.</summary>
    Unavailable = 3,
}

/// <param name="FailureReason">
/// Motivo de baixa cardinalidade, para log e métrica: <c>timeout</c>, <c>network</c>, <c>http_401</c>, <c>http_429</c>,
/// <c>http_4xx</c>, <c>http_5xx</c>, <c>invalid_response</c>. Nunca o corpo da resposta.
/// </param>
public sealed record PixChargeResultModel(PixChargeOutcome Outcome, string? Reference, string? CopyPasteCode, string? QrCodeImage, string? FailureReason)
{
    public static PixChargeResultModel Created(string reference, string copyPasteCode, string qrCodeImage)
        => new(PixChargeOutcome.Created, reference, copyPasteCode, qrCodeImage, null);

    public static PixChargeResultModel ProviderError(string reason)
        => new(PixChargeOutcome.ProviderError, null, null, null, reason);

    public static PixChargeResultModel Unavailable(string reason)
        => new(PixChargeOutcome.Unavailable, null, null, null, reason);
}
