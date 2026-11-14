using Infra.PostgreSql.Repositories.Payments.Models;

namespace Infra.PostgreSql.Repositories.Payments.Interfaces;

public interface IPaymentRouteRepository
{
    /// <summary>Rotas do produtor, em ordem de método e de prioridade.</summary>
    public Task<IReadOnlyList<PaymentRouteModel>> GetByProducerAsync(Guid producerId, CancellationToken cancellationToken = default);

    /// <summary>Rota de um método, em ordem de prioridade. Consulta exata por (produtor, método), sobre o índice da prioridade.</summary>
    public Task<IReadOnlyList<PaymentRouteModel>> GetByProducerAndMethodAsync(Guid producerId, string paymentMethod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Substitui a lista inteira do método (RN6). Substituições simultâneas do mesmo método são serializadas,
    /// para a última vencer em vez de violar a unicidade da prioridade.
    /// </summary>
    public Task ReplaceAsync(Guid producerId, string paymentMethod, IReadOnlyList<PaymentRouteModel> routes, CancellationToken cancellationToken = default);
}
