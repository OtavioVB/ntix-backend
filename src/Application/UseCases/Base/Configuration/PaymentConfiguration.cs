namespace Application.UseCases.Base.Configuration;

/// <summary>Regras da cobrança do pedido (REQ7).</summary>
public sealed record PaymentConfiguration
{
    /// <summary>O Pix termina esta folga antes do fim da reserva, para o pagamento do último segundo ainda achar os ingressos (RN5).</summary>
    public int ConfirmationGraceSeconds { get; init; } = 120;

    /// <summary>Validade mínima de um Pix no fornecedor (AbacatePay, 300 s). Com menos que isso a reserva não comporta a cobrança (RN5).</summary>
    public int PixMinimumSeconds { get; init; } = 300;

    /// <summary>No máximo este número de integrações tentadas por pagamento (RN7).</summary>
    public int MaxProviderAttempts { get; init; } = 3;
}
