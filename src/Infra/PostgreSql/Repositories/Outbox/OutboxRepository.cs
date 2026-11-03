using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Outbox;

public sealed class OutboxRepository : IOutboxRepository
{
    private const int EVENT_TYPE_MAX_LENGTH = 128;

    private const string INSERT_SQL = """
        INSERT INTO outbox.outbox_event (id_outbox_event, event_type, aggregate_id, payload, created_at, created_correlation_id)
        VALUES (@id_outbox_event, @event_type, @aggregate_id, @payload, @created_at, @created_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public OutboxRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task AddAsync(OutboxEventModel outboxEvent, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(OutboxRepository)}.{nameof(AddAsync)}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "outbox.outbox_event");

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_outbox_event", NpgsqlDbType.Uuid).Value = outboxEvent.Id;
        command.Parameters.Add("@event_type", NpgsqlDbType.Varchar, EVENT_TYPE_MAX_LENGTH).Value = outboxEvent.EventType;
        command.Parameters.Add("@aggregate_id", NpgsqlDbType.Uuid).Value = outboxEvent.AggregateId;
        command.Parameters.Add("@payload", NpgsqlDbType.Jsonb).Value = outboxEvent.Payload;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = outboxEvent.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = outboxEvent.CreatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
