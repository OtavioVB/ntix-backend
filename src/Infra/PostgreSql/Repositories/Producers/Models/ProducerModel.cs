namespace Infra.PostgreSql.Repositories.Producers.Models;

public sealed record ProducerModel(
    Guid Id,
    string Name,
    string Email,
    string Phone,
    string Slug,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);
