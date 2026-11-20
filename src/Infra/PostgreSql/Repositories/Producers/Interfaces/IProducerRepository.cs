using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Models;

namespace Infra.PostgreSql.Repositories.Producers.Interfaces;

public interface IProducerRepository
{
    /// <summary>Verdadeiro quando o e-mail já pertence a uma produtora ou a um usuário de produtora.</summary>
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Id da produtora pelo slug, consulta exata sobre <c>ux_producer_slug</c>. Nulo quando não existe.</summary>
    public Task<Guid?> GetIdBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Produtora pelo id, para o login e a renovação devolverem slug e status atuais. Nula quando não existe.</summary>
    public Task<ProducerModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Slug e nome comercial de várias produtoras de uma vez, para a vitrine (REQ11).</summary>
    public Task<IReadOnlyList<ProducerProfileModel>> GetProfilesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    public Task<ProducerCreationResult> CreateAsync(ProducerModel producer, CancellationToken cancellationToken = default);

    public Task<ProducerUserCreationResult> CreateUserAsync(ProducerUserModel producerUser, CancellationToken cancellationToken = default);
}
