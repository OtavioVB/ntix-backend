namespace Infra.PostgreSql.Repositories.Events.Models;

/// <summary>Linha de <c>events.event</c>, com tipos primitivos. Enums como texto controlado pela aplicação.</summary>
public sealed record EventModel(
    Guid Id,
    Guid ProducerId,
    string Title,
    string Slug,
    string? Description,
    string Category,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime? DoorsOpenAt,
    string VenueName,
    string Address,
    string City,
    string State,
    string AgeRating,
    string? ImageUrl,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime UpdatedAt,
    Guid UpdatedCorrelationId);
