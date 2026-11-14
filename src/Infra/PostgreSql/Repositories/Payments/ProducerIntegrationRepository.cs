using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Payments.Enums;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Npgsql;
using NpgsqlTypes;

using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Payments;

public sealed class ProducerIntegrationRepository : IProducerIntegrationRepository
{
    private const string CREATE_INTEGRATION_SAVEPOINT_NAME = "create_producer_integration";
    private const string UPDATE_INTEGRATION_SAVEPOINT_NAME = "update_producer_integration";

    private const int NAME_MAX_LENGTH = 64;
    private const int ENUM_MAX_LENGTH = 32;
    private const int CREDENTIAL_KEY_MAX_LENGTH = 64;
    private const int VALUE_HINT_MAX_LENGTH = 4;

    private const string SELECT_COLUMNS = """
        SELECT i.id_producer_integration, i.id_producer, i.id_provider, i.name, i.status,
               i.created_at, i.created_correlation_id, i.updated_at, i.updated_correlation_id,
               c.credential_key, c.encrypted_value, c.value_hint, c.updated_at, c.updated_correlation_id
        FROM payments.producer_integration i
        LEFT JOIN payments.producer_integration_credential c ON c.id_producer_integration = i.id_producer_integration
        """;

    private const string SELECT_BY_PRODUCER_SQL = SELECT_COLUMNS + """

        WHERE i.id_producer = @id_producer
        ORDER BY i.created_at, i.id_producer_integration, c.credential_key;
        """;

    // O id_producer na consulta é o isolamento entre produtores (RN1): a integração de outro produtor "não existe".
    private const string SELECT_BY_ID_FOR_UPDATE_SQL = SELECT_COLUMNS + """

        WHERE i.id_producer = @id_producer AND i.id_producer_integration = @id_producer_integration
        ORDER BY c.credential_key
        FOR UPDATE OF i;
        """;

    private const string INSERT_INTEGRATION_SQL = """
        INSERT INTO payments.producer_integration (id_producer_integration, id_producer, id_provider, name, status, created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_producer_integration, @id_producer, @id_provider, @name, @status, @created_at, @created_correlation_id, @updated_at, @updated_correlation_id);
        """;

    private const string UPDATE_INTEGRATION_SQL = """
        UPDATE payments.producer_integration
        SET name = @name, status = @status, updated_at = @updated_at, updated_correlation_id = @updated_correlation_id
        WHERE id_producer_integration = @id_producer_integration AND id_producer = @id_producer;
        """;

    private const string UPSERT_CREDENTIAL_SQL = """
        INSERT INTO payments.producer_integration_credential (id_producer_integration, credential_key, encrypted_value, value_hint, updated_at, updated_correlation_id)
        VALUES (@id_producer_integration, @credential_key, @encrypted_value, @value_hint, @updated_at, @updated_correlation_id)
        ON CONFLICT (id_producer_integration, credential_key) DO UPDATE
        SET encrypted_value = EXCLUDED.encrypted_value,
            value_hint = EXCLUDED.value_hint,
            updated_at = EXCLUDED.updated_at,
            updated_correlation_id = EXCLUDED.updated_correlation_id;
        """;

    private const string DELETE_CREDENTIALS_SQL = """
        DELETE FROM payments.producer_integration_credential
        WHERE id_producer_integration = @id_producer_integration AND credential_key = ANY(@credential_keys);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public ProducerIntegrationRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros, que aqui incluem credenciais cifradas.
    private static Activity? StartDatabaseActivity(string operation, string collection)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(ProducerIntegrationRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", collection);

    public async Task<IReadOnlyList<ProducerIntegrationModel>> GetByProducerAsync(Guid producerId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByProducerAsync), "payments.producer_integration");

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_PRODUCER_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;

        return await ReadIntegrationsAsync(command, cancellationToken);
    }

    public async Task<ProducerIntegrationModel?> GetByIdForUpdateAsync(Guid producerId, Guid integrationId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByIdForUpdateAsync), "payments.producer_integration");

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_ID_FOR_UPDATE_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = integrationId;

        return (await ReadIntegrationsAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<ProducerIntegrationCreationResult> CreateAsync(ProducerIntegrationModel integration, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync), "payments.producer_integration");

        await using var command = _unitOfWork.CreateCommand(INSERT_INTEGRATION_SQL);

        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = integration.Id;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = integration.ProducerId;
        command.Parameters.Add("@id_provider", NpgsqlDbType.Uuid).Value = integration.ProviderId;
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, NAME_MAX_LENGTH).Value = integration.Name;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = integration.Status;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = integration.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = integration.CreatedCorrelationId;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = integration.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = integration.UpdatedCorrelationId;

        // A violação do nome invalidaria a transação; o savepoint devolve o conflito sem perdê-la.
        await _unitOfWork.SaveAsync(CREATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(CREATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

            return ProducerIntegrationCreationResult.NameAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(CREATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

        foreach (var credential in integration.Credentials)
            await UpsertCredentialAsync(integration.Id, credential, cancellationToken);

        return ProducerIntegrationCreationResult.Created;
    }

    public async Task<ProducerIntegrationUpdateResult> UpdateAsync(
        ProducerIntegrationModel integration,
        IReadOnlyList<ProducerIntegrationCredentialModel> changedCredentials,
        IReadOnlyList<string> removedCredentialKeys,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(UpdateAsync), "payments.producer_integration");

        await using var command = _unitOfWork.CreateCommand(UPDATE_INTEGRATION_SQL);

        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = integration.Id;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = integration.ProducerId;
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, NAME_MAX_LENGTH).Value = integration.Name;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = integration.Status;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = integration.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = integration.UpdatedCorrelationId;

        await _unitOfWork.SaveAsync(UPDATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(UPDATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

            return ProducerIntegrationUpdateResult.NameAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(UPDATE_INTEGRATION_SAVEPOINT_NAME, cancellationToken);

        foreach (var credential in changedCredentials)
            await UpsertCredentialAsync(integration.Id, credential, cancellationToken);

        if (removedCredentialKeys.Count > 0)
            await DeleteCredentialsAsync(integration.Id, removedCredentialKeys, cancellationToken);

        return ProducerIntegrationUpdateResult.Updated;
    }

    private async Task UpsertCredentialAsync(Guid integrationId, ProducerIntegrationCredentialModel credential, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(nameof(UpsertCredentialAsync), "payments.producer_integration_credential");

        await using var command = _unitOfWork.CreateCommand(UPSERT_CREDENTIAL_SQL);

        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = integrationId;
        command.Parameters.Add("@credential_key", NpgsqlDbType.Varchar, CREDENTIAL_KEY_MAX_LENGTH).Value = credential.Key;
        command.Parameters.Add("@encrypted_value", NpgsqlDbType.Bytea).Value = credential.EncryptedValue;
        command.Parameters.Add("@value_hint", NpgsqlDbType.Varchar, VALUE_HINT_MAX_LENGTH).Value = (object?)credential.ValueHint ?? DBNull.Value;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = credential.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = credential.UpdatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteCredentialsAsync(Guid integrationId, IReadOnlyList<string> credentialKeys, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(nameof(DeleteCredentialsAsync), "payments.producer_integration_credential");

        await using var command = _unitOfWork.CreateCommand(DELETE_CREDENTIALS_SQL);

        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = integrationId;
        command.Parameters.Add("@credential_keys", NpgsqlDbType.Array | NpgsqlDbType.Varchar).Value = credentialKeys.ToArray();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Uma linha por credencial (LEFT JOIN): agrupa de volta por integração, mantendo a ordem da consulta.
    private static async Task<IReadOnlyList<ProducerIntegrationModel>> ReadIntegrationsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var integrations = new List<ProducerIntegrationModel>();
        var credentials = new Dictionary<Guid, List<ProducerIntegrationCredentialModel>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var integrationId = reader.GetGuid(0);

            if (!credentials.TryGetValue(integrationId, out var integrationCredentials))
            {
                integrationCredentials = [];
                credentials[integrationId] = integrationCredentials;

                integrations.Add(new ProducerIntegrationModel(
                    Id: integrationId,
                    ProducerId: reader.GetGuid(1),
                    ProviderId: reader.GetGuid(2),
                    Name: reader.GetString(3),
                    Status: reader.GetString(4),
                    CreatedAt: reader.GetDateTime(5),
                    CreatedCorrelationId: reader.GetGuid(6),
                    UpdatedAt: reader.GetDateTime(7),
                    UpdatedCorrelationId: reader.GetGuid(8),
                    Credentials: integrationCredentials));
            }

            if (await reader.IsDBNullAsync(9, cancellationToken))
                continue;

            integrationCredentials.Add(new ProducerIntegrationCredentialModel(
                Key: reader.GetString(9),
                EncryptedValue: reader.GetFieldValue<byte[]>(10),
                ValueHint: await reader.IsDBNullAsync(11, cancellationToken) ? null : reader.GetString(11),
                UpdatedAt: reader.GetDateTime(12),
                UpdatedCorrelationId: reader.GetGuid(13)));
        }

        return integrations;
    }
}
