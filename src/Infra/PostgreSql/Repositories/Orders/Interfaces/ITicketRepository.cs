using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Infra.PostgreSql.Repositories.Orders.Interfaces;

public interface ITicketRepository
{
    /// <summary>Grava os ingressos do pedido. Código repetido volta como resultado, sem invalidar a transação (savepoint).</summary>
    Task<TicketCreationResult> CreateManyAsync(IReadOnlyList<TicketModel> tickets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingressos emitidos do comprador, com o evento (REQ13, RN4, RN5). <paramref name="upcoming"/> verdadeiro: eventos que não terminaram,
    /// por início crescente; falso: eventos terminados, por início decrescente.
    /// </summary>
    Task<CustomerTicketPageModel> ListByCustomerAsync(Guid customerId, bool upcoming, DateTime now, int offset, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Ingresso do comprador pelo código, com o evento. O de outro comprador volta nulo, como inexistente (RN1).</summary>
    Task<CustomerTicketRowModel?> GetByCodeForCustomerAsync(string code, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Ingressos do pedido, com o lote de cada um (REQ8).</summary>
    Task<IReadOnlyList<TicketListItemModel>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
