using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Orders;

public sealed class SalesRepository : ISalesRepository
{
    private const string DEFAULT_PARTITION_NAME = "orders.sale_entry_default";
    private const int TIME_ZONE_MAX_LENGTH = 64;

    // Uma linha por item; o tableoid diz em que partição cada uma caiu.
    private const string INSERT_ENTRIES_SQL = """
        INSERT INTO orders.sale_entry (id_order_item, id_order, id_producer, id_event, id_ticket_batch, quantity, unit_price, paid_at, created_correlation_id)
        SELECT * FROM unnest(@id_order_items, @id_orders, @id_producers, @id_events, @id_ticket_batches, @quantities, @unit_prices, @paid_ats, @correlation_ids)
        RETURNING tableoid::regclass::text;
        """;

    // Soma atômica: duas efetivações simultâneas do mesmo lote não perdem incremento.
    private const string UPSERT_BATCH_COUNTERS_SQL = """
        INSERT INTO orders.ticket_batch_sales AS current (id_ticket_batch, id_producer, id_event, sold_quantity, gross_amount, paid_orders, updated_at)
        SELECT id_ticket_batch, @id_producer, @id_event, quantity, amount, 1, @updated_at
        FROM unnest(@id_ticket_batches, @quantities, @amounts) AS item(id_ticket_batch, quantity, amount)
        ON CONFLICT ON CONSTRAINT pk_ticket_batch_sales DO UPDATE
        SET sold_quantity = current.sold_quantity + EXCLUDED.sold_quantity,
            gross_amount = current.gross_amount + EXCLUDED.gross_amount,
            paid_orders = current.paid_orders + 1,
            updated_at = EXCLUDED.updated_at;
        """;

    private const string UPSERT_EVENT_COUNTER_SQL = """
        INSERT INTO orders.event_sales AS current (id_event, id_producer, sold_quantity, gross_amount, paid_orders, updated_at)
        VALUES (@id_event, @id_producer, @quantity, @amount, 1, @updated_at)
        ON CONFLICT ON CONSTRAINT pk_event_sales DO UPDATE
        SET sold_quantity = current.sold_quantity + EXCLUDED.sold_quantity,
            gross_amount = current.gross_amount + EXCLUDED.gross_amount,
            paid_orders = current.paid_orders + 1,
            updated_at = EXCLUDED.updated_at;
        """;

    // RN10: capacidade pelo índice dos lotes e vendas pelo contador do evento, sem ler pedidos.
    private const string SELECT_EVENT_SUMMARIES_SQL = """
        SELECT e.id_event,
               COALESCE((SELECT SUM(b.quantity) FROM events.ticket_batch b WHERE b.id_event = e.id_event AND b.id_producer = @id_producer), 0),
               COALESCE(s.sold_quantity, 0),
               COALESCE(s.gross_amount, 0),
               COALESCE(s.paid_orders, 0)
        FROM unnest(@id_events) AS e(id_event)
        LEFT JOIN orders.event_sales s ON s.id_event = e.id_event AND s.id_producer = @id_producer;
        """;

    private const string SELECT_BATCH_TOTALS_SQL = """
        SELECT id_ticket_batch, gross_amount, paid_orders
        FROM orders.ticket_batch_sales
        WHERE id_ticket_batch = ANY(@id_ticket_batches);
        """;

    // RN11: o filtro em paid_at poda as partições fora do intervalo.
    private const string SELECT_DAILY_SQL = """
        SELECT (paid_at AT TIME ZONE @time_zone)::date AS day,
               SUM(quantity),
               SUM(quantity * unit_price),
               COUNT(DISTINCT id_order)
        FROM orders.sale_entry
        WHERE id_event = @id_event AND paid_at >= @from AND paid_at < @before
        GROUP BY day
        ORDER BY day;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public SalesRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual, sem valores de parâmetros.
    private static Activity? StartDatabaseActivity(string operation, string collection)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(SalesRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", collection);

    public async Task<int> RegisterPaidOrderAsync(IReadOnlyList<SaleEntryModel> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return 0;

        var first = entries[0];
        var defaultPartitionRows = 0;

        using (StartDatabaseActivity(nameof(RegisterPaidOrderAsync), "orders.sale_entry"))
        {
            await using var command = _unitOfWork.CreateCommand(INSERT_ENTRIES_SQL);

            command.Parameters.Add("@id_order_items", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.OrderItemId).ToArray();
            command.Parameters.Add("@id_orders", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.OrderId).ToArray();
            command.Parameters.Add("@id_producers", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.ProducerId).ToArray();
            command.Parameters.Add("@id_events", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.EventId).ToArray();
            command.Parameters.Add("@id_ticket_batches", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.TicketBatchId).ToArray();
            command.Parameters.Add("@quantities", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = entries.Select(entry => entry.Quantity).ToArray();
            command.Parameters.Add("@unit_prices", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = entries.Select(entry => entry.UnitPrice).ToArray();
            command.Parameters.Add("@paid_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz).Value = entries.Select(entry => entry.PaidAt).ToArray();
            command.Parameters.Add("@correlation_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.CorrelationId).ToArray();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(0) == DEFAULT_PARTITION_NAME)
                    defaultPartitionRows++;
            }
        }

        // O índice único (pedido, lote) do item garante um item por lote: cada um conta como um pedido do lote.
        using (StartDatabaseActivity("UpsertBatchCounters", "orders.ticket_batch_sales"))
        {
            await using var command = _unitOfWork.CreateCommand(UPSERT_BATCH_COUNTERS_SQL);

            command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = first.ProducerId;
            command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = first.EventId;
            command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = first.PaidAt;
            command.Parameters.Add("@id_ticket_batches", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = entries.Select(entry => entry.TicketBatchId).ToArray();
            command.Parameters.Add("@quantities", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = entries.Select(entry => entry.Quantity).ToArray();
            command.Parameters.Add("@amounts", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = entries.Select(entry => entry.Quantity * entry.UnitPrice).ToArray();

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (StartDatabaseActivity("UpsertEventCounter", "orders.event_sales"))
        {
            await using var command = _unitOfWork.CreateCommand(UPSERT_EVENT_COUNTER_SQL);

            command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = first.EventId;
            command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = first.ProducerId;
            command.Parameters.Add("@quantity", NpgsqlDbType.Integer).Value = entries.Sum(entry => entry.Quantity);
            command.Parameters.Add("@amount", NpgsqlDbType.Numeric).Value = entries.Sum(entry => entry.Quantity * entry.UnitPrice);
            command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = first.PaidAt;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return defaultPartitionRows;
    }

    public async Task<IReadOnlyDictionary<Guid, EventSalesSummaryModel>> GetEventSummariesAsync(Guid producerId, IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        var summaries = new Dictionary<Guid, EventSalesSummaryModel>();

        var ids = eventIds.Distinct().ToArray();

        if (ids.Length == 0)
            return summaries;

        using var activity = StartDatabaseActivity(nameof(GetEventSummariesAsync), "orders.event_sales");

        await using var command = _unitOfWork.CreateCommand(SELECT_EVENT_SUMMARIES_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_events", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            summaries[reader.GetGuid(0)] = new EventSalesSummaryModel(
                Capacity: (int)reader.GetInt64(1),
                SoldQuantity: reader.GetInt32(2),
                GrossAmount: reader.GetDecimal(3),
                PaidOrders: reader.GetInt32(4));
        }

        return summaries;
    }

    public async Task<IReadOnlyDictionary<Guid, TicketBatchSalesTotalsModel>> GetBatchTotalsAsync(IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken = default)
    {
        var totals = batchIds.Distinct().ToDictionary(id => id, _ => new TicketBatchSalesTotalsModel(GrossAmount: 0m, PaidOrders: 0));

        if (totals.Count == 0)
            return totals;

        using var activity = StartDatabaseActivity(nameof(GetBatchTotalsAsync), "orders.ticket_batch_sales");

        await using var command = _unitOfWork.CreateCommand(SELECT_BATCH_TOTALS_SQL);

        command.Parameters.Add("@id_ticket_batches", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = totals.Keys.ToArray();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            totals[reader.GetGuid(0)] = new TicketBatchSalesTotalsModel(GrossAmount: reader.GetDecimal(1), PaidOrders: reader.GetInt32(2));

        return totals;
    }

    public async Task<IReadOnlyList<DailySalesModel>> GetDailySalesAsync(Guid eventId, DateTime fromUtc, DateTime beforeUtc, string timeZone, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetDailySalesAsync), "orders.sale_entry");

        await using var command = _unitOfWork.CreateCommand(SELECT_DAILY_SQL);

        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = eventId;
        command.Parameters.Add("@from", NpgsqlDbType.TimestampTz).Value = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        command.Parameters.Add("@before", NpgsqlDbType.TimestampTz).Value = DateTime.SpecifyKind(beforeUtc, DateTimeKind.Utc);
        command.Parameters.Add("@time_zone", NpgsqlDbType.Text, TIME_ZONE_MAX_LENGTH).Value = timeZone;

        var days = new List<DailySalesModel>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(new DailySalesModel(
                Date: DateOnly.FromDateTime(reader.GetDateTime(0)),
                SoldQuantity: (int)reader.GetInt64(1),
                GrossAmount: reader.GetDecimal(2),
                PaidOrders: (int)reader.GetInt64(3)));
        }

        return days;
    }
}
