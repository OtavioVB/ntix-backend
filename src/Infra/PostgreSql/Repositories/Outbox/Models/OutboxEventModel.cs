namespace Infra.PostgreSql.Repositories.Outbox.Models;

/// <param name="Payload">Evento serializado em JSON.</param>
public sealed record OutboxEventModel(
    Guid Id,
    string EventType,
    Guid AggregateId,
    string Payload,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);
