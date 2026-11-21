using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Models;

namespace Infra.PostgreSql.Repositories.Orders.Interfaces;

public interface IOrderRepository
{
    /// <summary>Grava o pedido e os itens; código já usado volta como resultado, sem invalidar a transação.</summary>
    Task<OrderCreationResult> CreateAsync(OrderModel order, CancellationToken cancellationToken = default);

    /// <summary>Pedido do cliente pelo código, com os itens. O de outro cliente volta nulo, como inexistente (RN11).</summary>
    Task<OrderModel?> GetByCodeAsync(string code, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Mesmo que <see cref="GetByCodeAsync"/>, travando o pedido até o fim da transação (pagamento, REQ7).</summary>
    Task<OrderModel?> GetByCodeForUpdateAsync(string code, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Pedido pelo id, travado até o fim da transação, com os itens. Usado pela confirmação do webhook (REQ8, RN6).</summary>
    Task<OrderModel?> GetByIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pedidos do comprador, do mais recente ao mais antigo, com o evento e a quantidade de ingressos (REQ13). <paramref name="statusFilter"/>
    /// é a chave do status efetivo (<c>PAID</c>, <c>PENDING_PAYMENT</c>, <c>EXPIRED</c>) ou nulo para todos.
    /// </summary>
    Task<CustomerOrderPageModel> ListByCustomerAsync(Guid customerId, string? statusFilter, DateTime now, int offset, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Grava a nova situação do pedido (vencimento da reserva, REQ7; pagamento, REQ8).</summary>
    Task UpdateStatusAsync(OrderModel order, CancellationToken cancellationToken = default);
}
