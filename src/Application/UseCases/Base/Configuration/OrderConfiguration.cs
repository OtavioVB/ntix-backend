namespace Application.UseCases.Base.Configuration;

/// <summary>Regras do pedido (REQ6) e da vitrine (REQ11), que usa o mesmo limite por pedido.</summary>
public sealed record OrderConfiguration
{
    /// <summary>No máximo este número de ingressos por pedido; também o <c>maxSelectable</c> de cada lote na vitrine.</summary>
    public int MaxTicketsPerOrder { get; init; } = 6;

    /// <summary>Minutos que os ingressos do pedido ficam reservados à espera do pagamento (REQ6, RN9).</summary>
    public int ReservationMinutes { get; init; } = 15;
}
