using System.Security.Cryptography;
using System.Text;

namespace UnitTests.Support;

/// <summary>
/// Webhook do AbacatePay para os testes (REQ8). A chave é de teste: a assinatura é HMAC-SHA256 do corpo bruto com ela, como a
/// assinatura real com a chave pública. Nunca chama o AbacatePay.
/// </summary>
public static class WebhookTestData
{
    public const string PUBLIC_KEY = "chave-publica-de-teste-do-webhook";

    /// <summary>Segredo da integração de teste (WEBHOOK_SECRET, 32 caracteres ou mais).</summary>
    public const string WEBHOOK_SECRET = "segredo-de-teste-do-webhook-32chars";

    public static string Sign(byte[] rawBody)
        => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(PUBLIC_KEY), rawBody));

    /// <summary>Envelope do AbacatePay no formato da documentação (campos usados pela Ntix).</summary>
    public static byte[] Envelope(string eventId, string transparentId, string eventType = "transparent.completed", string status = "PAID")
        => Encoding.UTF8.GetBytes(
            "{\"id\":\"" + eventId + "\",\"event\":\"" + eventType + "\",\"apiVersion\":2,\"devMode\":true,"
            + "\"data\":{\"transparent\":{\"id\":\"" + transparentId + "\",\"externalId\":\"x\",\"amount\":24000,\"status\":\"" + status + "\"}}}");
}
