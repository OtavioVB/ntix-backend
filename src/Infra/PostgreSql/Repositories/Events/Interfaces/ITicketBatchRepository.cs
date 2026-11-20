using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Models;

namespace Infra.PostgreSql.Repositories.Events.Interfaces;

public interface ITicketBatchRepository
{
    /// <summary>Grava o lote; nome já usado no evento volta como resultado, sem invalidar a transação.</summary>
    Task<TicketBatchWriteResult> CreateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default);

    /// <summary>Lotes do evento do produtor, por início da venda e preço. Os de outro produtor não aparecem (RN1).</summary>
    Task<IReadOnlyList<TicketBatchModel>> ListByEventAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Lote do evento do produtor, travado até o fim da transação (a mesma trava que a reserva do REQ6 usa).</summary>
    Task<TicketBatchModel?> GetByIdForUpdateAsync(Guid producerId, Guid eventId, Guid batchId, CancellationToken cancellationToken = default);

    Task<TicketBatchWriteResult> UpdateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vendidos e reservados por lote, para a disponibilidade. Até os pedidos existirem (REQ6), todos os lotes têm zero;
    /// o REQ6 passa a somar os itens dos pedidos.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TicketBatchSalesModel>> GetSalesAsync(IReadOnlyCollection<Guid> batchIds, DateTime now, CancellationToken cancellationToken = default);
}
