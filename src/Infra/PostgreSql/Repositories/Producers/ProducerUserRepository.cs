using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Producers;

public sealed class ProducerUserRepository : IProducerUserRepository
{
    private const int EMAIL_MAX_LENGTH = 256;

    private const string SELECT_BY_EMAIL_SQL = """
        SELECT id_producer_user, id_producer, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id
        FROM producers.producer_user
        WHERE email = @email;
        """;

    private const string SELECT_BY_ID_SQL = """
        SELECT id_producer_user, id_producer, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id
        FROM producers.producer_user
        WHERE id_producer_user = @id_producer_user;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public ProducerUserRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros, porque o e-mail é dado pessoal.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(ProducerUserRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "producers.producer_user");

    public async Task<ProducerUserModel?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByEmailAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_EMAIL_SQL);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = email;

        return await ReadSingleAsync(command, cancellationToken);
    }

    public async Task<ProducerUserModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByIdAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_ID_SQL);

        command.Parameters.Add("@id_producer_user", NpgsqlDbType.Uuid).Value = id;

        return await ReadSingleAsync(command, cancellationToken);
    }

    private static async Task<ProducerUserModel?> ReadSingleAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ProducerUserModel(
            Id: reader.GetGuid(0),
            ProducerId: reader.GetGuid(1),
            Name: reader.GetString(2),
            Email: reader.GetString(3),
            PasswordHash: reader.GetFieldValue<byte[]>(4),
            PasswordSalt: reader.GetFieldValue<byte[]>(5),
            UserType: reader.GetString(6),
            Status: reader.GetString(7),
            CreatedAt: reader.GetDateTime(8),
            CreatedCorrelationId: reader.GetGuid(9));
    }
}
