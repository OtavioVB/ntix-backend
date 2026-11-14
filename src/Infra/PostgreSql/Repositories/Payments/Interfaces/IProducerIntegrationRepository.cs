using Infra.PostgreSql.Repositories.Payments.Enums;
using Infra.PostgreSql.Repositories.Payments.Models;

namespace Infra.PostgreSql.Repositories.Payments.Interfaces;

public interface IProducerIntegrationRepository
{
    /// <summary>Integrações do produtor, com as credenciais cifradas, em ordem de criação.</summary>
    public Task<IReadOnlyList<ProducerIntegrationModel>> GetByProducerAsync(Guid producerId, CancellationToken cancellationToken = default);

    /// <summary>Integração do produtor, travada até o fim da transação. Nula quando não existe ou é de outro produtor.</summary>
    public Task<ProducerIntegrationModel?> GetByIdForUpdateAsync(Guid producerId, Guid integrationId, CancellationToken cancellationToken = default);

    /// <summary>Grava a integração e as credenciais dela.</summary>
    public Task<ProducerIntegrationCreationResult> CreateAsync(ProducerIntegrationModel integration, CancellationToken cancellationToken = default);

    /// <summary>Atualiza nome e situação, grava as credenciais alteradas e remove as chaves informadas.</summary>
    public Task<ProducerIntegrationUpdateResult> UpdateAsync(
        ProducerIntegrationModel integration,
        IReadOnlyList<ProducerIntegrationCredentialModel> changedCredentials,
        IReadOnlyList<string> removedCredentialKeys,
        CancellationToken cancellationToken = default);
}
