using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Customers;

public sealed class CustomerSessionRepository : ICustomerSessionRepository
{
    private const int REFRESH_TOKEN_HASH_LENGTH = 32;

    private const string INSERT_SQL = """
        INSERT INTO customers.customer_session (id_customer_session, id_customer, id_family, refresh_token_hash, expires_at, created_at, created_correlation_id, revoked_at, id_replaced_by)
        VALUES (@id_customer_session, @id_customer, @id_family, @refresh_token_hash, @expires_at, @created_at, @created_correlation_id, NULL, NULL);
        """;

    private const string SELECT_BY_REFRESH_TOKEN_HASH_FOR_UPDATE_SQL = """
        SELECT id_customer_session, id_customer, id_family, refresh_token_hash, expires_at, created_at, created_correlation_id, revoked_at, id_replaced_by
        FROM customers.customer_session
        WHERE refresh_token_hash = @refresh_token_hash
        FOR UPDATE;
        """;

    private const string MARK_REPLACED_SQL = """
        UPDATE customers.customer_session
        SET revoked_at = @revoked_at, id_replaced_by = @id_replaced_by
        WHERE id_customer_session = @id_customer_session AND revoked_at IS NULL;
        """;

    private const string REVOKE_FAMILY_SQL = """
        UPDATE customers.customer_session
        SET revoked_at = @revoked_at
        WHERE id_family = @id_family AND revoked_at IS NULL;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public CustomerSessionRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros, porque o hash identifica a sessão.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(CustomerSessionRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "customers.customer_session");

    public async Task CreateAsync(CustomerSessionModel session, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync));

        await InsertAsync(session, cancellationToken);
    }

    public async Task<CustomerSessionModel?> GetByRefreshTokenHashAsync(byte[] refreshTokenHash, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByRefreshTokenHashAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_REFRESH_TOKEN_HASH_FOR_UPDATE_SQL);

        command.Parameters.Add("@refresh_token_hash", NpgsqlDbType.Bytea, REFRESH_TOKEN_HASH_LENGTH).Value = refreshTokenHash;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CustomerSessionModel(
            Id: reader.GetGuid(0),
            CustomerId: reader.GetGuid(1),
            FamilyId: reader.GetGuid(2),
            RefreshTokenHash: reader.GetFieldValue<byte[]>(3),
            ExpiresAt: reader.GetDateTime(4),
            CreatedAt: reader.GetDateTime(5),
            CreatedCorrelationId: reader.GetGuid(6),
            RevokedAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            ReplacedById: reader.IsDBNull(8) ? null : reader.GetGuid(8));
    }

    public async Task ReplaceAsync(CustomerSessionModel replaced, CustomerSessionModel next, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ReplaceAsync));

        // A próxima entra antes, porque id_replaced_by aponta para ela.
        await InsertAsync(next, cancellationToken);

        await using var command = _unitOfWork.CreateCommand(MARK_REPLACED_SQL);

        command.Parameters.Add("@id_customer_session", NpgsqlDbType.Uuid).Value = replaced.Id;
        command.Parameters.Add("@revoked_at", NpgsqlDbType.TimestampTz).Value = replaced.RevokedAt ?? throw new ArgumentException("The replaced session must be revoked.", nameof(replaced));
        command.Parameters.Add("@id_replaced_by", NpgsqlDbType.Uuid).Value = next.Id;

        // A sessão foi lida ativa e travada com FOR UPDATE: outro total de linhas é defeito, não regra de negócio.
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The session to be replaced is no longer active.");
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(RevokeFamilyAsync));

        await using var command = _unitOfWork.CreateCommand(REVOKE_FAMILY_SQL);

        command.Parameters.Add("@id_family", NpgsqlDbType.Uuid).Value = familyId;
        command.Parameters.Add("@revoked_at", NpgsqlDbType.TimestampTz).Value = revokedAt;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertAsync(CustomerSessionModel session, CancellationToken cancellationToken)
    {
        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_customer_session", NpgsqlDbType.Uuid).Value = session.Id;
        command.Parameters.Add("@id_customer", NpgsqlDbType.Uuid).Value = session.CustomerId;
        command.Parameters.Add("@id_family", NpgsqlDbType.Uuid).Value = session.FamilyId;
        command.Parameters.Add("@refresh_token_hash", NpgsqlDbType.Bytea, REFRESH_TOKEN_HASH_LENGTH).Value = session.RefreshTokenHash;
        command.Parameters.Add("@expires_at", NpgsqlDbType.TimestampTz).Value = session.ExpiresAt;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = session.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = session.CreatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
