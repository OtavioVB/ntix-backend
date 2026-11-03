using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Producers;

public sealed class ProducerRepository : IProducerRepository
{
    private const string UNIQUE_SLUG_INDEX_NAME = "ux_producer_slug";
    private const string CREATE_PRODUCER_SAVEPOINT_NAME = "create_producer";
    private const string CREATE_PRODUCER_USER_SAVEPOINT_NAME = "create_producer_user";

    private const int NAME_MAX_LENGTH = 256;
    private const int EMAIL_MAX_LENGTH = 256;
    private const int PHONE_MAX_LENGTH = 16;
    private const int SLUG_MAX_LENGTH = 63;
    private const int ENUM_MAX_LENGTH = 32;

    private const string EXISTS_BY_EMAIL_SQL = """
        SELECT EXISTS (SELECT 1 FROM producers.producer WHERE email = @email)
            OR EXISTS (SELECT 1 FROM producers.producer_user WHERE email = @email);
        """;

    private const string EXISTS_BY_SLUG_SQL = """
        SELECT EXISTS (SELECT 1 FROM producers.producer WHERE slug = @slug);
        """;

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, @phone, @slug, @status, @created_at, @created_correlation_id);
        """;

    private const string INSERT_PRODUCER_USER_SQL = """
        INSERT INTO producers.producer_user (id_producer_user, id_producer, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id)
        VALUES (@id_producer_user, @id_producer, @name, @email, @password_hash, @password_salt, @user_type, @status, @created_at, @created_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public ProducerRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros nem a mensagem da PostgresException, que traz o valor duplicado.
    private static Activity? StartDatabaseActivity(string operation, string collection)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(ProducerRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", collection);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsByEmailAsync), "producers.producer");

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_EMAIL_SQL);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = email;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsBySlugAsync), "producers.producer");

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_SLUG_SQL);

        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = slug;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<ProducerCreationResult> CreateAsync(ProducerModel producer, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync), "producers.producer");

        await using var command = _unitOfWork.CreateCommand(INSERT_PRODUCER_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producer.Id;
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, NAME_MAX_LENGTH).Value = producer.Name;
        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = producer.Email;
        command.Parameters.Add("@phone", NpgsqlDbType.Varchar, PHONE_MAX_LENGTH).Value = producer.Phone;
        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = producer.Slug;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = producer.Status;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = producer.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = producer.CreatedCorrelationId;

        // A violação de índice invalidaria a transação; voltar ao savepoint mantém a nova tentativa na mesma transação.
        await _unitOfWork.SaveAsync(CREATE_PRODUCER_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(CREATE_PRODUCER_SAVEPOINT_NAME, cancellationToken);

            return ex.ConstraintName == UNIQUE_SLUG_INDEX_NAME
                ? ProducerCreationResult.SlugAlreadyExists
                : ProducerCreationResult.EmailAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(CREATE_PRODUCER_SAVEPOINT_NAME, cancellationToken);

        return ProducerCreationResult.Created;
    }

    public async Task<ProducerUserCreationResult> CreateUserAsync(ProducerUserModel producerUser, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateUserAsync), "producers.producer_user");

        await using var command = _unitOfWork.CreateCommand(INSERT_PRODUCER_USER_SQL);

        command.Parameters.Add("@id_producer_user", NpgsqlDbType.Uuid).Value = producerUser.Id;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerUser.ProducerId;
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, NAME_MAX_LENGTH).Value = producerUser.Name;
        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = producerUser.Email;
        command.Parameters.Add("@password_hash", NpgsqlDbType.Bytea).Value = producerUser.PasswordHash;
        command.Parameters.Add("@password_salt", NpgsqlDbType.Bytea).Value = producerUser.PasswordSalt;
        command.Parameters.Add("@user_type", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = producerUser.UserType;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = producerUser.Status;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = producerUser.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = producerUser.CreatedCorrelationId;

        await _unitOfWork.SaveAsync(CREATE_PRODUCER_USER_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(CREATE_PRODUCER_USER_SAVEPOINT_NAME, cancellationToken);

            return ProducerUserCreationResult.EmailAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(CREATE_PRODUCER_USER_SAVEPOINT_NAME, cancellationToken);

        return ProducerUserCreationResult.Created;
    }
}
