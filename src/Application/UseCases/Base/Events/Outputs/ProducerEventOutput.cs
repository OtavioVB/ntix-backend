namespace Application.UseCases.Base.Events.Outputs;

/// <summary>Evento como o painel do produtor o vê (REQ9). Datas em UTC; campos opcionais ausentes ficam fora do JSON.</summary>
public sealed record ProducerEventOutput(
    Guid Id,
    string Slug,
    string Title,
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
    DateTime UpdatedAt);
