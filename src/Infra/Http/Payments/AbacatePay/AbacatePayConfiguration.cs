namespace Infra.Http.Payments.AbacatePay;

public sealed record AbacatePayConfiguration
{
    public string BaseUrl { get; init; } = "https://api.abacatepay.com/v2";

    /// <summary>Tempo máximo de cada tentativa (REQ7, RN7). Estourado, vale a próxima integração.</summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Chave pública do AbacatePay para a assinatura do webhook (REQ8, RN1). Não é segredo, mas fica vazia até ser cadastrada em
    /// appsettings: sem ela, toda assinatura é recusada (fecha por padrão).
    /// </summary>
    public string WebhookPublicKey { get; init; } = string.Empty;
}
