using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Events;

public sealed class TicketBatchRepository : ITicketBatchRepository
{
    private const string WRITE_BATCH_SAVEPOINT_NAME = "write_ticket_batch";

    private const int NAME_MAX_LENGTH = 64;
    private const int NOTE_MAX_LENGTH = 140;
    private const int ENUM_MAX_LENGTH = 32;

    private const string SELECT_COLUMNS = """
        SELECT id_ticket_batch, id_event, id_producer, name, note, price, quantity, sales_starts_at, sales_ends_at, status,
               created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM events.ticket_batch
        """;

    // O id_producer na consulta é o isolamento entre produtores (RN1).
    private const string SELECT_BY_EVENT_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND id_event = @id_event
        ORDER BY sales_starts_at, price, id_ticket_batch;
        """;

    private const string SELECT_BY_EVENT_IDS_SQL = SELECT_COLUMNS + """

        WHERE id_event = ANY(@id_events)
        ORDER BY id_event, sales_starts_at, price, id_ticket_batch;
        """;

    private const string SELECT_BY_ID_FOR_UPDATE_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND id_event = @id_event AND id_ticket_batch = @id_ticket_batch
        FOR UPDATE;
        """;

    // Trava dos lotes do pedido (REQ6, RN8), em ordem de id: dois pedidos com os mesmos lotes travam na mesma ordem e não geram deadlock.
    // O filtro por evento deixa de fora o lote de outro evento (RN4).
    private const string SELECT_BY_IDS_FOR_UPDATE_SQL = SELECT_COLUMNS + """

        WHERE id_event = @id_event AND id_ticket_batch = ANY(@id_ticket_batches)
        ORDER BY id_ticket_batch
        FOR UPDATE;
        """;

    // Vendidos: itens de pedidos pagos. Reservados: itens de pedidos aguardando pagamento com a reserva ainda valendo (RN8);
    // os vencidos não contam, sem rotina que os devolva ao estoque (RN12).
    private const string SELECT_SALES_SQL = """
        SELECT item.id_ticket_batch,
               COALESCE(SUM(item.quantity) FILTER (WHERE o.status = 'PAID'), 0),
               COALESCE(SUM(item.quantity) FILTER (WHERE o.status = 'PENDING_PAYMENT' AND o.expires_at > @now), 0)
        FROM orders.order_item item
        JOIN orders.orders o ON o.id_order = item.id_order
        WHERE item.id_ticket_batch = ANY(@id_ticket_batches)
        GROUP BY item.id_ticket_batch;
        """;

    private const string INSERT_SQL = """
        INSERT INTO events.ticket_batch (id_ticket_batch, id_event, id_producer, name, note, price, quantity, sales_starts_at, sales_ends_at, status,
                                         created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_ticket_batch, @id_event, @id_producer, @name, @note, @price, @quantity, @sales_starts_at, @sales_ends_at, @status,
                @created_at, @created_correlation_id, @updated_at, @updated_correlation_id);
        """;

    private const string UPDATE_SQL = """
        UPDATE events.ticket_batch
        SET name = @name, note = @note, price = @price, quantity = @quantity, sales_starts_at = @sales_starts_at,
            sales_ends_at = @sales_ends_at, status = @status, updated_at = @updated_at, updated_correlation_id = @updated_correlation_id
        WHERE id_producer = @id_producer AND id_event = @id_event AND id_ticket_batch = @id_ticket_batch;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public TicketBatchRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual, sem valores de parâmetros.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketBatchRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "events.ticket_batch");

    public Task<TicketBatchWriteResult> CreateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default)
        => WriteAsync(INSERT_SQL, nameof(CreateAsync), batch, includeCreation: true, cancellationToken);

    public Task<TicketBatchWriteResult> UpdateAsync(TicketBatchModel batch, CancellationToken cancellationToken = default)
        => WriteAsync(UPDATE_SQL, nameof(UpdateAsync), batch, includeCreation: false, cancellationToken);

    public async Task<IReadOnlyList<TicketBatchModel>> ListByEventAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ListByEventAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_EVENT_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = eventId;

        return await ReadBatchesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketBatchModel>> ListByEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        if (eventIds.Count == 0)
            return [];

        using var activity = StartDatabaseActivity(nameof(ListByEventIdsAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_EVENT_IDS_SQL);

        command.Parameters.Add("@id_events", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = eventIds.ToArray();

        return await ReadBatchesAsync(command, cancellationToken);
    }

    public async Task<TicketBatchModel?> GetByIdForUpdateAsync(Guid producerId, Guid eventId, Guid batchId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByIdForUpdateAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_ID_FOR_UPDATE_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = eventId;
        command.Parameters.Add("@id_ticket_batch", NpgsqlDbType.Uuid).Value = batchId;

        return (await ReadBatchesAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<TicketBatchModel>> ListByIdsForUpdateAsync(Guid eventId, IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ListByIdsForUpdateAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_IDS_FOR_UPDATE_SQL);

        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = eventId;
        command.Parameters.Add("@id_ticket_batches", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = batchIds.ToArray();

        return await ReadBatchesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, TicketBatchSalesModel>> GetSalesAsync(IReadOnlyCollection<Guid> batchIds, DateTime now, CancellationToken cancellationToken = default)
    {
        var sales = batchIds.Distinct().ToDictionary(id => id, _ => new TicketBatchSalesModel(Sold: 0, Reserved: 0));

        if (sales.Count == 0)
            return sales;

        using var activity = StartDatabaseActivity(nameof(GetSalesAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_SALES_SQL);

        command.Parameters.Add("@id_ticket_batches", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = sales.Keys.ToArray();
        command.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            sales[reader.GetGuid(0)] = new TicketBatchSalesModel(Sold: (int)reader.GetInt64(1), Reserved: (int)reader.GetInt64(2));

        return sales;
    }

    private async Task<TicketBatchWriteResult> WriteAsync(string sql, string operation, TicketBatchModel batch, bool includeCreation, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(operation);

        await using var command = _unitOfWork.CreateCommand(sql);

        command.Parameters.Add("@id_ticket_batch", NpgsqlDbType.Uuid).Value = batch.Id;
        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = batch.EventId;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = batch.ProducerId;
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, NAME_MAX_LENGTH).Value = batch.Name;
        command.Parameters.Add("@note", NpgsqlDbType.Varchar, NOTE_MAX_LENGTH).Value = (object?)batch.Note ?? DBNull.Value;
        command.Parameters.Add("@price", NpgsqlDbType.Numeric).Value = batch.Price;
        command.Parameters.Add("@quantity", NpgsqlDbType.Integer).Value = batch.Quantity;
        command.Parameters.Add("@sales_starts_at", NpgsqlDbType.TimestampTz).Value = batch.SalesStartsAt;
        command.Parameters.Add("@sales_ends_at", NpgsqlDbType.TimestampTz).Value = batch.SalesEndsAt;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = batch.Status;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = batch.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = batch.UpdatedCorrelationId;

        if (includeCreation)
        {
            command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = batch.CreatedAt;
            command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = batch.CreatedCorrelationId;
        }

        // A violação do nome invalidaria a transação; o savepoint devolve o conflito sem perdê-la.
        await _unitOfWork.SaveAsync(WRITE_BATCH_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(WRITE_BATCH_SAVEPOINT_NAME, cancellationToken);

            return TicketBatchWriteResult.NameAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(WRITE_BATCH_SAVEPOINT_NAME, cancellationToken);

        return TicketBatchWriteResult.Written;
    }

    private static async Task<IReadOnlyList<TicketBatchModel>> ReadBatchesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var batches = new List<TicketBatchModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            batches.Add(new TicketBatchModel(
                Id: reader.GetGuid(0),
                EventId: reader.GetGuid(1),
                ProducerId: reader.GetGuid(2),
                Name: reader.GetString(3),
                Note: await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                Price: reader.GetDecimal(5),
                Quantity: reader.GetInt32(6),
                SalesStartsAt: reader.GetDateTime(7),
                SalesEndsAt: reader.GetDateTime(8),
                Status: reader.GetString(9),
                CreatedAt: reader.GetDateTime(10),
                CreatedCorrelationId: reader.GetGuid(11),
                UpdatedAt: reader.GetDateTime(12),
                UpdatedCorrelationId: reader.GetGuid(13)));
        }

        return batches;
    }
}
