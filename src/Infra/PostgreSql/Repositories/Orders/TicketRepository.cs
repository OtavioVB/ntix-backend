using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Orders;

public sealed class TicketRepository : ITicketRepository
{
    private const string DB_SYSTEM_TAG = "db.system.name";
    private const string DB_SYSTEM_VALUE = "postgresql";
    private const string DB_COLLECTION_TAG = "db.collection.name";
    private const string TICKET_COLLECTION = "orders.ticket";
    private const string ID_CUSTOMER_PARAM = "@id_customer";
    private const string WRITE_TICKET_SAVEPOINT_NAME = "write_ticket";
    private const string CODE_UNIQUE_INDEX_NAME = "ux_ticket_code";

    private const int CODE_MAX_LENGTH = 12;
    private const int HOLDER_NAME_MAX_LENGTH = 256;
    private const int STATUS_MAX_LENGTH = 16;

    private const string INSERT_SQL = """
        INSERT INTO orders.ticket (id_ticket, id_order_item, id_order, id_customer, id_event, code, holder_name, status, created_at, created_correlation_id)
        VALUES (@id_ticket, @id_order_item, @id_order, @id_customer, @id_event, @code, @holder_name, @status, @created_at, @created_correlation_id);
        """;

    private const string SELECT_BY_ORDER_SQL = """
        SELECT t.code, i.batch_name, t.holder_name, t.status
        FROM orders.ticket t
        JOIN orders.order_item i ON i.id_order_item = t.id_order_item
        WHERE t.id_order = @id_order
        ORDER BY i.batch_name, t.code;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public TicketRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TicketCreationResult> CreateManyAsync(IReadOnlyList<TicketModel> tickets, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketRepository)}.{nameof(CreateManyAsync)}", ActivityKind.Client)
            ?.SetTag(DB_SYSTEM_TAG, DB_SYSTEM_VALUE)
            .SetTag(DB_COLLECTION_TAG, TICKET_COLLECTION);

        // A colisão do código invalidaria a transação; o savepoint devolve o conflito e mantém o pedido em aberto.
        await _unitOfWork.SaveAsync(WRITE_TICKET_SAVEPOINT_NAME, cancellationToken);

        foreach (var ticket in tickets)
        {
            try
            {
                await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

                command.Parameters.Add("@id_ticket", NpgsqlDbType.Uuid).Value = ticket.Id;
                command.Parameters.Add("@id_order_item", NpgsqlDbType.Uuid).Value = ticket.OrderItemId;
                command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = ticket.OrderId;
                command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = ticket.CustomerId;
                command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = ticket.EventId;
                command.Parameters.Add("@code", NpgsqlDbType.Varchar, CODE_MAX_LENGTH).Value = ticket.Code;
                command.Parameters.Add("@holder_name", NpgsqlDbType.Varchar, HOLDER_NAME_MAX_LENGTH).Value = ticket.HolderName;
                command.Parameters.Add("@status", NpgsqlDbType.Varchar, STATUS_MAX_LENGTH).Value = ticket.Status;
                command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = ticket.CreatedAt;
                command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = ticket.CreatedCorrelationId;

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == CODE_UNIQUE_INDEX_NAME)
            {
                await _unitOfWork.RollbackToSavepointAsync(WRITE_TICKET_SAVEPOINT_NAME, cancellationToken);

                return TicketCreationResult.CodeAlreadyExists;
            }
        }

        await _unitOfWork.ReleaseSavepointAsync(WRITE_TICKET_SAVEPOINT_NAME, cancellationToken);

        return TicketCreationResult.Created;
    }

    // Mesma junção para lista e detalhe: o evento vem na consulta, sem N+1 (REQ13). Textos fixos; o período só escolhe entre dois.
    private const string CUSTOMER_TICKET_SELECT = """
        SELECT t.code, t.status, i.batch_name, t.holder_name, o.code, t.created_at, o.id_producer,
               e.slug, e.title, e.starts_at, e.doors_open_at, e.venue_name, e.address, e.city, e.state, e.age_rating, e.image_url
        FROM orders.ticket t
        JOIN orders.order_item i ON i.id_order_item = t.id_order_item
        JOIN orders.orders o ON o.id_order = t.id_order
        JOIN events.event e ON e.id_event = t.id_event
        """;

    private const string CUSTOMER_TICKET_UPCOMING = " WHERE t.id_customer = @id_customer AND e.ends_at > @now";
    private const string CUSTOMER_TICKET_PAST = " WHERE t.id_customer = @id_customer AND e.ends_at <= @now";
    private const string CUSTOMER_TICKET_UPCOMING_ORDER = " ORDER BY e.starts_at ASC, t.code ASC LIMIT @page_size OFFSET @offset;";
    private const string CUSTOMER_TICKET_PAST_ORDER = " ORDER BY e.starts_at DESC, t.code DESC LIMIT @page_size OFFSET @offset;";

    private const string CUSTOMER_TICKET_COUNT = """
        SELECT COUNT(*)::int
        FROM orders.ticket t
        JOIN events.event e ON e.id_event = t.id_event
        """;

    private const string CUSTOMER_TICKET_BY_CODE = " WHERE t.code = @code AND t.id_customer = @id_customer;";

    public async Task<CustomerTicketPageModel> ListByCustomerAsync(Guid customerId, bool upcoming, DateTime now, int offset, int pageSize, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketRepository)}.{nameof(ListByCustomerAsync)}", ActivityKind.Client)
            ?.SetTag(DB_SYSTEM_TAG, DB_SYSTEM_VALUE)
            .SetTag(DB_COLLECTION_TAG, TICKET_COLLECTION);

        var where = upcoming ? CUSTOMER_TICKET_UPCOMING : CUSTOMER_TICKET_PAST;
        var order = upcoming ? CUSTOMER_TICKET_UPCOMING_ORDER : CUSTOMER_TICKET_PAST_ORDER;

        int totalItems;

        await using (var command = _unitOfWork.CreateCommand(CUSTOMER_TICKET_COUNT + where + ";"))
        {
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;
            command.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;

            totalItems = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        }

        var items = new List<CustomerTicketRowModel>();

        await using (var command = _unitOfWork.CreateCommand(CUSTOMER_TICKET_SELECT + where + order))
        {
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;
            command.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;
            command.Parameters.Add("@page_size", NpgsqlDbType.Integer).Value = pageSize;
            command.Parameters.Add("@offset", NpgsqlDbType.Integer).Value = offset;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadCustomerTicket(reader));
        }

        return new CustomerTicketPageModel(items, totalItems);
    }

    public async Task<CustomerTicketRowModel?> GetByCodeForCustomerAsync(string code, Guid customerId, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketRepository)}.{nameof(GetByCodeForCustomerAsync)}", ActivityKind.Client)
            ?.SetTag(DB_SYSTEM_TAG, DB_SYSTEM_VALUE)
            .SetTag(DB_COLLECTION_TAG, TICKET_COLLECTION);

        await using var command = _unitOfWork.CreateCommand(CUSTOMER_TICKET_SELECT + CUSTOMER_TICKET_BY_CODE);

        command.Parameters.Add("@code", NpgsqlDbType.Varchar, CODE_MAX_LENGTH).Value = code;
        command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadCustomerTicket(reader);
    }

    private static CustomerTicketRowModel ReadCustomerTicket(NpgsqlDataReader reader)
        => new(
            Code: reader.GetString(0),
            Status: reader.GetString(1),
            BatchName: reader.GetString(2),
            HolderName: reader.GetString(3),
            OrderCode: reader.GetString(4),
            IssuedAt: reader.GetDateTime(5),
            ProducerId: reader.GetGuid(6),
            EventSlug: reader.GetString(7),
            EventTitle: reader.GetString(8),
            EventStartsAt: reader.GetDateTime(9),
            DoorsOpenAt: reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            VenueName: reader.GetString(11),
            Address: reader.GetString(12),
            City: reader.GetString(13),
            State: reader.GetString(14),
            AgeRating: reader.GetString(15),
            EventImageUrl: reader.IsDBNull(16) ? null : reader.GetString(16));

    public async Task<IReadOnlyList<TicketListItemModel>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketRepository)}.{nameof(ListByOrderAsync)}", ActivityKind.Client)
            ?.SetTag(DB_SYSTEM_TAG, DB_SYSTEM_VALUE)
            .SetTag(DB_COLLECTION_TAG, TICKET_COLLECTION);

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_ORDER_SQL);

        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tickets = new List<TicketListItemModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            tickets.Add(new TicketListItemModel(
                Code: reader.GetString(0),
                BatchName: reader.GetString(1),
                HolderName: reader.GetString(2),
                Status: reader.GetString(3)));
        }

        return tickets;
    }
}
