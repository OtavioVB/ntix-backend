namespace Infra.PostgreSql.Repositories.Events.Models;

/// <summary>Filtros da vitrine (REQ11) com tipos primitivos. Nulo é filtro ausente.</summary>
public sealed record EventSearchModel(
    Guid? ProducerId,
    string? Category,
    string? City,
    string? State,
    DateTime? StartsFrom,
    DateTime? StartsTo,
    int Offset,
    int Limit);

/// <param name="TotalItems">Total de eventos que atendem aos filtros, sem a paginação.</param>
public sealed record EventSearchResultModel(IReadOnlyList<EventModel> Items, int TotalItems);
