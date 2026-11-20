using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Customers;

public sealed class ProducerCustomerRepository : IProducerCustomerRepository
{
    // O vínculo é o primeiro pedido do cliente com o produtor: pedidos simultâneos disputam a mesma chave e só um cria.
    private const string INSERT_SQL = """
        INSERT INTO customers.producer_customer (id_producer, id_customer, created_at, created_correlation_id)
        VALUES (@id_producer, @id_customer, @created_at, @created_correlation_id)
        ON CONFLICT ON CONSTRAINT pk_producer_customer DO NOTHING;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public ProducerCustomerRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> LinkAsync(Guid producerId, Guid customerId, DateTime createdAt, Guid createdCorrelationId, CancellationToken cancellationToken = default)
    {
        // Span manual, sem valores de parâmetros.
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(ProducerCustomerRepository)}.{nameof(LinkAsync)}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "customers.producer_customer");

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_customer", NpgsqlDbType.Uuid).Value = customerId;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = createdAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = createdCorrelationId;

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
