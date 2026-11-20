using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Infra.PostgreSql.Repositories.Orders.Interfaces;

public interface IOrderRepository
{
    /// <summary>Grava o pedido e os itens; código já usado volta como resultado, sem invalidar a transação.</summary>
    Task<OrderCreationResult> CreateAsync(OrderModel order, CancellationToken cancellationToken = default);

    /// <summary>Pedido do cliente pelo código, com os itens. O de outro cliente volta nulo, como inexistente (RN11).</summary>
    Task<OrderModel?> GetByCodeAsync(string code, Guid customerId, CancellationToken cancellationToken = default);
}
