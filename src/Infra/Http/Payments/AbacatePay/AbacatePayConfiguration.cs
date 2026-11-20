namespace Infra.Http.Payments.AbacatePay;

public sealed record AbacatePayConfiguration
{
    public string BaseUrl { get; init; } = "https://api.abacatepay.com/v2";

    /// <summary>Tempo máximo de cada tentativa (REQ7, RN7). Estourado, vale a próxima integração.</summary>
    public int TimeoutSeconds { get; init; } = 5;
}
