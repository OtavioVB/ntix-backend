using System.Security.Cryptography;
using System.Text;

namespace Domain.BoundedContexts.PaymentContext.Services;

/// <summary>
/// Assinatura do webhook (REQ8, RN1): HMAC-SHA256, em Base64, do corpo bruto recebido, com a chave pública do fornecedor.
/// Comparação em tempo constante. Verificada antes de ler o JSON.
/// </summary>
public static class WebhookSignature
{
    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? signature, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(publicKey))
            return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(publicKey), rawBody);
        var expectedBase64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(expected));
        var received = Encoding.UTF8.GetBytes(signature.Trim());

        return received.Length == expectedBase64.Length && CryptographicOperations.FixedTimeEquals(received, expectedBase64);
    }

    /// <summary>RN3: o segredo da integração é comparado em tempo constante; sem segredo cadastrado, o caso de uso não chama isto.</summary>
    public static bool SecretMatches(string? received, string expected)
    {
        if (string.IsNullOrEmpty(received))
            return false;

        var receivedBytes = Encoding.UTF8.GetBytes(received);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return receivedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(receivedBytes, expectedBytes);
    }
}
