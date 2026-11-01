namespace Infra.SqlServer.Repositories.Producers.Models;

public sealed record ProducerModel(Guid Id, string Name, string Email, string Phone, string? Document, string Slug, string Status, DateTime CreatedAt, Guid CreatedCorrelationId);
