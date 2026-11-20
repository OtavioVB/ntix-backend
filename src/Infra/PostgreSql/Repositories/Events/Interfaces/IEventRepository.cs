using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Models;

namespace Infra.PostgreSql.Repositories.Events.Interfaces;

public interface IEventRepository
{
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Grava o evento; o slug já usado (criação simultânea) volta como resultado, sem invalidar a transação.</summary>
    Task<EventCreationResult> CreateAsync(EventModel @event, CancellationToken cancellationToken = default);

    /// <summary>Evento do produtor; o de outro produtor volta nulo, como inexistente (RN1).</summary>
    Task<EventModel?> GetByIdAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Mesmo que <see cref="GetByIdAsync"/>, travando a linha até o fim da transação.</summary>
    Task<EventModel?> GetByIdForUpdateAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Eventos do produtor por início crescente; <paramref name="status"/> nulo traz todos.</summary>
    Task<IReadOnlyList<EventModel>> ListByProducerAsync(Guid producerId, string? status, CancellationToken cancellationToken = default);

    /// <summary>Substitui os campos editáveis. Slug, situação e criação não mudam.</summary>
    Task UpdateAsync(EventModel @event, CancellationToken cancellationToken = default);

    /// <summary>Grava a nova situação do evento (publicação, REQ10).</summary>
    Task UpdateStatusAsync(EventModel @event, CancellationToken cancellationToken = default);
}
