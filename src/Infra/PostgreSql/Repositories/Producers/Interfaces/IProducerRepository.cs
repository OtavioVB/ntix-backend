using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Models;

namespace Infra.PostgreSql.Repositories.Producers.Interfaces;

public interface IProducerRepository
{
    /// <summary>Verdadeiro quando o e-mail já pertence a uma produtora ou a um usuário de produtora.</summary>
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    public Task<ProducerCreationResult> CreateAsync(ProducerModel producer, CancellationToken cancellationToken = default);

    public Task<ProducerUserCreationResult> CreateUserAsync(ProducerUserModel producerUser, CancellationToken cancellationToken = default);
}
