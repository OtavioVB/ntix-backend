using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Infra.PostgreSql.Repositories.Orders.Interfaces;

public interface ITicketRepository
{
    /// <summary>Grava os ingressos do pedido. Código repetido volta como resultado, sem invalidar a transação (savepoint).</summary>
    Task<TicketCreationResult> CreateManyAsync(IReadOnlyList<TicketModel> tickets, CancellationToken cancellationToken = default);

    /// <summary>Ingressos do pedido, com o lote de cada um (REQ8).</summary>
    Task<IReadOnlyList<TicketListItemModel>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
