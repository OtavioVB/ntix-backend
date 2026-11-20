using Infra.PostgreSql.Repositories.Customers.Models;

namespace Infra.PostgreSql.Repositories.Customers.Interfaces;

public interface ICustomerSessionRepository
{
    public Task CreateAsync(CustomerSessionModel session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessão pelo hash do refresh token, consulta exata sobre <c>ux_customer_session_refresh_token_hash</c>, travada
    /// até o fim da transação: duas renovações com o mesmo token não substituem a mesma sessão. Nula quando não existe.
    /// </summary>
    public Task<CustomerSessionModel?> GetByRefreshTokenHashAsync(byte[] refreshTokenHash, CancellationToken cancellationToken = default);

    /// <summary>Grava a próxima sessão e marca a atual como revogada e substituída por ela, na mesma transação.</summary>
    public Task ReplaceAsync(CustomerSessionModel replaced, CustomerSessionModel next, CancellationToken cancellationToken = default);

    /// <summary>Revoga as sessões ainda não revogadas da família. Devolve quantas foram revogadas.</summary>
    public Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken cancellationToken = default);
}
