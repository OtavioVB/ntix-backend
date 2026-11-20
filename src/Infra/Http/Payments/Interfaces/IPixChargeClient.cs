using Infra.Http.Payments.Models;

namespace Infra.Http.Payments.Interfaces;

/// <summary>Adaptador de um fornecedor que gera cobrança Pix (REQ7). O fornecedor novo entra com outra implementação, sem mudar o fluxo.</summary>
public interface IPixChargeClient
{
    /// <summary>Código do fornecedor no catálogo (<c>payments.provider.code</c>), pelo qual o caso de uso escolhe o adaptador.</summary>
    string ProviderCode { get; }

    /// <summary>
    /// Gera a cobrança com a chave do produtor. Falha esperada (recusa, tempo esgotado, resposta inválida) volta no resultado,
    /// sem exceção. O cancelamento de <paramref name="cancellationToken"/> propaga.
    /// </summary>
    Task<PixChargeResultModel> CreatePixChargeAsync(string apiKey, PixChargeRequestModel request, CancellationToken cancellationToken = default);
}
