namespace Application.UseCases.Base.Configuration;

/// <summary>
/// Regras do pedido (REQ6) que a vitrine (REQ11) já precisa: no máximo <see cref="MaxTicketsPerOrder"/> ingressos por pedido,
/// o mesmo limite usado para o <c>maxSelectable</c> de cada lote.
/// </summary>
public sealed record OrderConfiguration
{
    public int MaxTicketsPerOrder { get; init; } = 6;
}
