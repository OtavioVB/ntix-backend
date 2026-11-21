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
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "orders.ticket");

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
                command.Parameters.Add("@id_customer", NpgsqlDbType.Uuid).Value = ticket.CustomerId;
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

    public async Task<IReadOnlyList<TicketListItemModel>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(TicketRepository)}.{nameof(ListByOrderAsync)}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "orders.ticket");

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
