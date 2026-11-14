using Infra.PostgreSql.Repositories.Producers.Models;

namespace Infra.PostgreSql.Repositories.Producers.Interfaces;

public interface IProducerUserRepository
{
    /// <summary>Usuário pelo e-mail normalizado, consulta exata sobre <c>ux_producer_user_email</c>. Nulo quando não existe.</summary>
    public Task<ProducerUserModel?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Usuário pelo id, relido na renovação da sessão para conferir o status atual. Nulo quando não existe.</summary>
    public Task<ProducerUserModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
