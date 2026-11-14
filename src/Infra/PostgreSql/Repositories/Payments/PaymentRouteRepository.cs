using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Payments;

public sealed class PaymentRouteRepository : IPaymentRouteRepository
{
    private const int ENUM_MAX_LENGTH = 32;

    private const string SELECT_COLUMNS = """
        SELECT id_producer_payment_route, id_producer, payment_method, priority, id_producer_integration, updated_at, updated_correlation_id
        FROM payments.producer_payment_route
        """;

    private const string SELECT_BY_PRODUCER_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer
        ORDER BY payment_method, priority;
        """;

    private const string SELECT_BY_PRODUCER_AND_METHOD_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND payment_method = @payment_method
        ORDER BY priority;
        """;

    // Trava só o par (produtor, método) até o fim da transação: outra substituição do mesmo método espera esta terminar
    // e então apaga o que ela gravou, em vez de colidir no índice único da prioridade.
    private const string LOCK_ROUTE_SQL = """
        SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));
        """;

    private const string DELETE_ROUTE_SQL = """
        DELETE FROM payments.producer_payment_route
        WHERE id_producer = @id_producer AND payment_method = @payment_method;
        """;

    private const string INSERT_ROUTE_SQL = """
        INSERT INTO payments.producer_payment_route (id_producer_payment_route, id_producer, payment_method, priority, id_producer_integration, updated_at, updated_correlation_id)
        VALUES (@id_producer_payment_route, @id_producer, @payment_method, @priority, @id_producer_integration, @updated_at, @updated_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public PaymentRouteRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(PaymentRouteRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "payments.producer_payment_route");

    public async Task<IReadOnlyList<PaymentRouteModel>> GetByProducerAsync(Guid producerId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByProducerAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_PRODUCER_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;

        return await ReadRoutesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentRouteModel>> GetByProducerAndMethodAsync(Guid producerId, string paymentMethod, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByProducerAndMethodAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_PRODUCER_AND_METHOD_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@payment_method", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = paymentMethod;

        return await ReadRoutesAsync(command, cancellationToken);
    }

    public async Task ReplaceAsync(Guid producerId, string paymentMethod, IReadOnlyList<PaymentRouteModel> routes, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ReplaceAsync));

        await using (var lockCommand = _unitOfWork.CreateCommand(LOCK_ROUTE_SQL))
        {
            lockCommand.Parameters.Add("@lock_key", NpgsqlDbType.Text).Value = $"payments.producer_payment_route:{producerId}:{paymentMethod}";

            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCommand = _unitOfWork.CreateCommand(DELETE_ROUTE_SQL))
        {
            deleteCommand.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
            deleteCommand.Parameters.Add("@payment_method", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = paymentMethod;

            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var route in routes)
        {
            await using var insertCommand = _unitOfWork.CreateCommand(INSERT_ROUTE_SQL);

            insertCommand.Parameters.Add("@id_producer_payment_route", NpgsqlDbType.Uuid).Value = route.Id;
            insertCommand.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = route.ProducerId;
            insertCommand.Parameters.Add("@payment_method", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = route.PaymentMethod;
            insertCommand.Parameters.Add("@priority", NpgsqlDbType.Smallint).Value = route.Priority;
            insertCommand.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = route.IntegrationId;
            insertCommand.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = route.UpdatedAt;
            insertCommand.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = route.UpdatedCorrelationId;

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<PaymentRouteModel>> ReadRoutesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var routes = new List<PaymentRouteModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            routes.Add(new PaymentRouteModel(
                Id: reader.GetGuid(0),
                ProducerId: reader.GetGuid(1),
                PaymentMethod: reader.GetString(2),
                Priority: reader.GetInt16(3),
                IntegrationId: reader.GetGuid(4),
                UpdatedAt: reader.GetDateTime(5),
                UpdatedCorrelationId: reader.GetGuid(6)));
        }

        return routes;
    }
}
