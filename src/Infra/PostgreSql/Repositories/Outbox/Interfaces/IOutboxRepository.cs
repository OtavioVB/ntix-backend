using Infra.PostgreSql.Repositories.Outbox.Models;

namespace Infra.PostgreSql.Repositories.Outbox.Interfaces;

public interface IOutboxRepository
{
    /// <summary>Grava o evento na transação aberta, para ele só existir se a operação que o originou for confirmada.</summary>
    public Task AddAsync(OutboxEventModel outboxEvent, CancellationToken cancellationToken = default);
}
