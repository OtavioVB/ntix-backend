namespace Infra.PostgreSql.Repositories.Customers.Interfaces;

public interface IProducerCustomerRepository
{
    /// <summary>
    /// Vincula o cliente ao produtor no primeiro pedido (REQ6, RN13). Idempotente: devolve verdadeiro só quando criou o vínculo.
    /// </summary>
    Task<bool> LinkAsync(Guid producerId, Guid customerId, DateTime createdAt, Guid createdCorrelationId, CancellationToken cancellationToken = default);
}
