using Infra.PostgreSql.Repositories.Payments.Models;

namespace Infra.PostgreSql.Repositories.Payments.Interfaces;

public interface IPaymentRepository
{
    Task CreateAsync(PaymentModel payment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobrança mais recente do pedido, de qualquer situação. Como só pode haver uma <c>PENDING</c> por pedido
    /// (<c>ux_payment_order_pending</c>) e ela é sempre a mais nova, é também a pendente, se houver.
    /// </summary>
    Task<PaymentModel?> GetLatestByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Grava a nova situação da cobrança (vencimento, REQ7; pagamento, REQ8).</summary>
    Task UpdateStatusAsync(PaymentModel payment, CancellationToken cancellationToken = default);
}
