using Infra.PostgreSql.Repositories.Payments.Models;

namespace Infra.PostgreSql.Repositories.Payments.Interfaces;

public interface IPaymentWebhookEventRepository
{
    /// <summary>
    /// Registra o evento do envelope (RN5). Devolve falso quando o mesmo envelope já foi registrado, sem invalidar a transação:
    /// é a deduplicação, e quem recebe o falso não reprocessa.
    /// </summary>
    Task<bool> CreateAsync(PaymentWebhookEventModel webhookEvent, CancellationToken cancellationToken = default);
}
