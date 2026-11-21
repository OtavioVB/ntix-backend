using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Orders;

public sealed class OrderRepository : IOrderRepository
{
    private const string ID_CUSTOMER_PARAM = "@id_customer";
    private const string WRITE_ORDER_SAVEPOINT_NAME = "write_order";
    private const string CODE_UNIQUE_CONSTRAINT_NAME = "ux_orders_code";

    private const int CODE_MAX_LENGTH = 10;
    private const int ENUM_MAX_LENGTH = 32;
    private const int SALES_CHANNEL_MAX_LENGTH = 16;
    private const int BATCH_NAME_MAX_LENGTH = 64;

    private const string INSERT_ORDER_SQL = """
        INSERT INTO orders.orders (id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
                                   created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_order, @code, @id_producer, @id_event, @id_customer, @sales_channel, @total_amount, @status, @expires_at, @paid_at,
                @created_at, @created_correlation_id, @updated_at, @updated_correlation_id);
        """;

    private const string INSERT_ITEM_SQL = """
        INSERT INTO orders.order_item (id_order_item, id_order, id_ticket_batch, batch_name, quantity, unit_price)
        VALUES (@id_order_item, @id_order, @id_ticket_batch, @batch_name, @quantity, @unit_price);
        """;

    // O id_customer na consulta é o isolamento entre compradores (RN11).
    private const string SELECT_BY_CODE_SQL = """
        SELECT id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
               created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM orders.orders
        WHERE code = @code AND id_customer = @id_customer;
        """;

    private const string SELECT_BY_CODE_FOR_UPDATE_SQL = """
        SELECT id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
               created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM orders.orders
        WHERE code = @code AND id_customer = @id_customer
        FOR UPDATE;
        """;

    private const string SELECT_BY_ID_FOR_UPDATE_SQL = """
        SELECT id_order, code, id_producer, id_event, id_customer, sales_channel, total_amount, status, expires_at, paid_at,
               created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM orders.orders
        WHERE id_order = @id_order
        FOR UPDATE;
        """;

    private const string UPDATE_STATUS_SQL = """
        UPDATE orders.orders
        SET status = @status, paid_at = @paid_at, updated_at = @updated_at, updated_correlation_id = @updated_correlation_id
        WHERE id_order = @id_order;
        """;

    private const string SELECT_ITEMS_SQL = """
        SELECT id_order_item, id_ticket_batch, batch_name, quantity, unit_price
        FROM orders.order_item
        WHERE id_order = @id_order
        ORDER BY batch_name, id_order_item;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public OrderRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual, sem valores de parâmetros.
    private static Activity? StartDatabaseActivity(string operation, string collection)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(OrderRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", collection);

    public async Task<OrderCreationResult> CreateAsync(OrderModel order, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync), "orders.orders");

        // A colisão do código invalidaria a transação; o savepoint devolve o conflito sem perdê-la.
        await _unitOfWork.SaveAsync(WRITE_ORDER_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await using var command = _unitOfWork.CreateCommand(INSERT_ORDER_SQL);

            command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = order.Id;
            command.Parameters.Add("@code", NpgsqlDbType.Varchar, CODE_MAX_LENGTH).Value = order.Code;
            command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = order.ProducerId;
            command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = order.EventId;
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = order.CustomerId;
            command.Parameters.Add("@sales_channel", NpgsqlDbType.Varchar, SALES_CHANNEL_MAX_LENGTH).Value = order.SalesChannel;
            command.Parameters.Add("@total_amount", NpgsqlDbType.Numeric).Value = order.TotalAmount;
            command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = order.Status;
            command.Parameters.Add("@expires_at", NpgsqlDbType.TimestampTz).Value = order.ExpiresAt;
            command.Parameters.Add("@paid_at", NpgsqlDbType.TimestampTz).Value = (object?)order.PaidAt ?? DBNull.Value;
            command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = order.CreatedAt;
            command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = order.CreatedCorrelationId;
            command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = order.UpdatedAt;
            command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = order.UpdatedCorrelationId;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == CODE_UNIQUE_CONSTRAINT_NAME)
        {
            await _unitOfWork.RollbackToSavepointAsync(WRITE_ORDER_SAVEPOINT_NAME, cancellationToken);

            return OrderCreationResult.CodeAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(WRITE_ORDER_SAVEPOINT_NAME, cancellationToken);

        foreach (var item in order.Items)
            await InsertItemAsync(order.Id, item, cancellationToken);

        return OrderCreationResult.Created;
    }

    public Task<OrderModel?> GetByCodeAsync(string code, Guid customerId, CancellationToken cancellationToken = default)
        => ReadByCodeAsync(SELECT_BY_CODE_SQL, nameof(GetByCodeAsync), code, customerId, cancellationToken);

    public Task<OrderModel?> GetByCodeForUpdateAsync(string code, Guid customerId, CancellationToken cancellationToken = default)
        => ReadByCodeAsync(SELECT_BY_CODE_FOR_UPDATE_SQL, nameof(GetByCodeForUpdateAsync), code, customerId, cancellationToken);

    public async Task<OrderModel?> GetByIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByIdForUpdateAsync), "orders.orders");

        OrderModel? order;

        await using (var command = _unitOfWork.CreateCommand(SELECT_BY_ID_FOR_UPDATE_SQL))
        {
            command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;

            order = await ReadOrderAsync(command, cancellationToken);
        }

        if (order is null)
            return null;

        return order with { Items = await ListItemsAsync(order.Id, cancellationToken) };
    }

    public async Task<CustomerOrderPageModel> ListByCustomerAsync(Guid customerId, string? statusFilter, DateTime now, int offset, int pageSize, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ListByCustomerAsync), "orders.orders");

        var filter = CustomerOrdersQueries.FilterFor(statusFilter);
        var listSql = CustomerOrdersQueries.SELECT_ORDERS + " " + CustomerOrdersQueries.FROM_ORDERS + filter + CustomerOrdersQueries.ORDER_BY_CREATED;
        var countSql = CustomerOrdersQueries.COUNT_ORDERS + CustomerOrdersQueries.FROM_ORDERS + filter + ";";

        int totalItems;

        await using (var command = _unitOfWork.CreateCommand(countSql))
        {
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;
            command.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;

            totalItems = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        }

        var items = new List<CustomerOrderRowModel>();

        await using (var command = _unitOfWork.CreateCommand(listSql))
        {
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;
            command.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;
            command.Parameters.Add("@page_size", NpgsqlDbType.Integer).Value = pageSize;
            command.Parameters.Add("@offset", NpgsqlDbType.Integer).Value = offset;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CustomerOrderRowModel(
                    Code: reader.GetString(0),
                    Status: reader.GetString(1),
                    ExpiresAt: reader.GetDateTime(2),
                    CreatedAt: reader.GetDateTime(3),
                    PaidAt: await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetDateTime(4),
                    TotalAmount: reader.GetDecimal(5),
                    TicketCount: reader.GetInt32(6),
                    ProducerId: reader.GetGuid(7),
                    EventSlug: reader.GetString(8),
                    EventTitle: reader.GetString(9),
                    EventStartsAt: reader.GetDateTime(10),
                    EventCity: reader.GetString(11),
                    EventState: reader.GetString(12),
                    EventImageUrl: await reader.IsDBNullAsync(13, cancellationToken) ? null : reader.GetString(13)));
            }
        }

        return new CustomerOrderPageModel(items, totalItems);
    }

    public async Task UpdateStatusAsync(OrderModel order, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(UpdateStatusAsync), "orders.orders");

        await using var command = _unitOfWork.CreateCommand(UPDATE_STATUS_SQL);

        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = order.Id;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = order.Status;
        command.Parameters.Add("@paid_at", NpgsqlDbType.TimestampTz).Value = (object?)order.PaidAt ?? DBNull.Value;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = order.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = order.UpdatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<OrderModel?> ReadByCodeAsync(string sql, string operation, string code, Guid customerId, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(operation, "orders.orders");

        OrderModel? order;

        await using (var command = _unitOfWork.CreateCommand(sql))
        {
            command.Parameters.Add("@code", NpgsqlDbType.Varchar, CODE_MAX_LENGTH).Value = code;
            command.Parameters.Add(ID_CUSTOMER_PARAM, NpgsqlDbType.Uuid).Value = customerId;

            order = await ReadOrderAsync(command, cancellationToken);
        }

        if (order is null)
            return null;

        // Uma conexão só abre um leitor por vez: os itens são lidos depois de fechar o leitor do pedido.
        return order with { Items = await ListItemsAsync(order.Id, cancellationToken) };
    }

    private async Task InsertItemAsync(Guid orderId, OrderItemModel item, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(nameof(InsertItemAsync), "orders.order_item");

        await using var command = _unitOfWork.CreateCommand(INSERT_ITEM_SQL);

        command.Parameters.Add("@id_order_item", NpgsqlDbType.Uuid).Value = item.Id;
        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;
        command.Parameters.Add("@id_ticket_batch", NpgsqlDbType.Uuid).Value = item.BatchId;
        command.Parameters.Add("@batch_name", NpgsqlDbType.Varchar, BATCH_NAME_MAX_LENGTH).Value = item.BatchName;
        command.Parameters.Add("@quantity", NpgsqlDbType.Integer).Value = item.Quantity;
        command.Parameters.Add("@unit_price", NpgsqlDbType.Numeric).Value = item.UnitPrice;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<OrderItemModel>> ListItemsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(nameof(ListItemsAsync), "orders.order_item");

        await using var command = _unitOfWork.CreateCommand(SELECT_ITEMS_SQL);

        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var items = new List<OrderItemModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new OrderItemModel(
                Id: reader.GetGuid(0),
                BatchId: reader.GetGuid(1),
                BatchName: reader.GetString(2),
                Quantity: reader.GetInt32(3),
                UnitPrice: reader.GetDecimal(4)));
        }

        return items;
    }

    private static async Task<OrderModel?> ReadOrderAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new OrderModel(
            Id: reader.GetGuid(0),
            Code: reader.GetString(1),
            ProducerId: reader.GetGuid(2),
            EventId: reader.GetGuid(3),
            CustomerId: reader.GetGuid(4),
            SalesChannel: reader.GetString(5),
            TotalAmount: reader.GetDecimal(6),
            Status: reader.GetString(7),
            ExpiresAt: reader.GetDateTime(8),
            PaidAt: await reader.IsDBNullAsync(9, cancellationToken) ? null : reader.GetDateTime(9),
            CreatedAt: reader.GetDateTime(10),
            CreatedCorrelationId: reader.GetGuid(11),
            UpdatedAt: reader.GetDateTime(12),
            UpdatedCorrelationId: reader.GetGuid(13),
            Items: []);
    }
}
