namespace WebApi.Controllers.Payloads;

/// <summary>Corpo da criação e da edição de evento (REQ9). A edição substitui todos os campos: opcional ausente fica nulo.</summary>
/// <param name="Category">SHOWS, FESTIVALS, THEATER, STAND_UP, ELECTRONIC, SPORTS, GASTRONOMY, KIDS ou OTHER.</param>
/// <param name="StartsAt">ISO 8601 em UTC (ex.: 2027-03-20T18:00:00.000Z).</param>
/// <param name="State">Sigla da UF.</param>
/// <param name="AgeRating">FREE, AGE_10, AGE_12, AGE_14, AGE_16 ou AGE_18.</param>
public sealed record EventV1Request(
    string? Title,
    string? Description,
    string? Category,
    string? StartsAt,
    string? EndsAt,
    string? DoorsOpenAt,
    string? VenueName,
    string? Address,
    string? City,
    string? State,
    string? AgeRating,
    string? ImageUrl);
