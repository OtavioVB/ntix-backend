using Infra.Http.Payments.Models;

namespace Infra.Http.Payments.Interfaces;

/// <summary>Adaptador de um fornecedor que gera cobrança Pix (REQ7) e informa a situação dela (REQ8). O fornecedor novo entra com outra implementação, sem mudar o fluxo.</summary>
public interface IPixChargeClient
{
    /// <summary>Código do fornecedor no catálogo (<c>payments.provider.code</c>), pelo qual o caso de uso escolhe o adaptador.</summary>
    string ProviderCode { get; }

    /// <summary>
    /// Gera a cobrança com a chave do produtor. Falha esperada (recusa, tempo esgotado, resposta inválida) volta no resultado,
    /// sem exceção. O cancelamento de <paramref name="cancellationToken"/> propaga.
    /// </summary>
    Task<PixChargeResultModel> CreatePixChargeAsync(string apiKey, PixChargeRequestModel request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta a situação da cobrança na fonte, com a chave da integração que a gerou (REQ8, RN4). Só a resposta da fonte confirma
    /// um pagamento: o corpo do webhook nunca basta. Falha de rede, tempo esgotado ou resposta inválida volta como indisponível.
    /// </summary>
    Task<PixChargeStatusResultModel> GetPixChargeStatusAsync(string apiKey, string providerReference, CancellationToken cancellationToken = default);
}
