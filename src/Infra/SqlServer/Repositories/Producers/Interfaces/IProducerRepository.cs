using Infra.SqlServer.Repositories.Producers.Enums;
using Infra.SqlServer.Repositories.Producers.Models;

namespace Infra.SqlServer.Repositories.Producers.Interfaces;

public interface IProducerRepository
{
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByDocumentAsync(string document, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ProducerCreationResult> CreateAsync(ProducerModel producer, CancellationToken cancellationToken = default);
    Task<ProducerUserCreationResult> CreateUserAsync(ProducerUserModel user, CancellationToken cancellationToken = default);
    Task CreateEmailVerificationAsync(EmailVerificationModel emailVerification, CancellationToken cancellationToken = default);
}
