using Infra.SqlServer.Base.UnitOfWork;
using Infra.SqlServer.Repositories.Producers.Enums;
using Infra.SqlServer.Repositories.Producers.Interfaces;
using Infra.SqlServer.Repositories.Producers.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;

namespace Infra.SqlServer.Repositories.Producers;

public sealed class ProducerRepository : IProducerRepository
{
    private const int UNIQUE_INDEX_VIOLATION_ERROR_NUMBER = 2601;
    private const string UNIQUE_SLUG_INDEX_NAME = "UX_Producer_Slug";
    private const string UNIQUE_DOCUMENT_INDEX_NAME = "UX_Producer_Document";

    private const int NAME_MAX_LENGTH = 256;
    private const int EMAIL_MAX_LENGTH = 256;
    private const int PHONE_MAX_LENGTH = 16;
    private const int DOCUMENT_MAX_LENGTH = 14;
    private const int SLUG_MAX_LENGTH = 262;
    private const int STATUS_MAX_LENGTH = 16;
    private const int USER_TYPE_MAX_LENGTH = 16;
    private const int PASSWORD_HASH_LENGTH = 32;
    private const int PASSWORD_SALT_LENGTH = 16;
    private const int TOKEN_HASH_LENGTH = 32;

    private const string EXISTS_BY_EMAIL_SQL = """
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM producers.Producer WHERE email = @email)
              OR EXISTS (SELECT 1 FROM producers.ProducerUser WHERE email = @email)
            THEN 1 ELSE 0
        END;
        """;

    private const string EXISTS_BY_DOCUMENT_SQL = """
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM producers.Producer WHERE document = @document)
            THEN 1 ELSE 0
        END;
        """;

    private const string EXISTS_BY_SLUG_SQL = """
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM producers.Producer WHERE slug = @slug)
            THEN 1 ELSE 0
        END;
        """;

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.Producer (id_producer, name, email, phone, document, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, @phone, @document, @slug, @status, @created_at, @created_correlation_id);
        """;

    private const string INSERT_USER_SQL = """
        INSERT INTO producers.ProducerUser (id_producer_user, producer_id, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id)
        VALUES (@id_producer_user, @producer_id, @name, @email, @password_hash, @password_salt, @user_type, @status, @created_at, @created_correlation_id);
        """;

    private const string INSERT_EMAIL_VERIFICATION_SQL = """
        INSERT INTO producers.EmailVerification (id_email_verification, producer_user_id, token_hash, expires_at, created_at, created_correlation_id)
        VALUES (@id_email_verification, @producer_user_id, @token_hash, @expires_at, @created_at, @created_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.SqlServer");

    private readonly SqlServerUnitOfWork _unitOfWork;

    public ProducerRepository(SqlServerUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros nem a mensagem da SqlException, que traz o valor duplicado.
    private static Activity? StartDatabaseActivity(string operation, string collection)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(ProducerRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "microsoft.sql.server")
            .SetTag("db.collection.name", collection);

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsByEmailAsync), "producers.Producer");

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_EMAIL_SQL);

        command.Parameters.Add("@email", SqlDbType.NVarChar, EMAIL_MAX_LENGTH).Value = email;

        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    public async Task<bool> ExistsByDocumentAsync(string document, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsByDocumentAsync), "producers.Producer");

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_DOCUMENT_SQL);

        command.Parameters.Add("@document", SqlDbType.VarChar, DOCUMENT_MAX_LENGTH).Value = document;

        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsBySlugAsync), "producers.Producer");

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_SLUG_SQL);

        command.Parameters.Add("@slug", SqlDbType.VarChar, SLUG_MAX_LENGTH).Value = slug;

        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    public async Task<ProducerCreationResult> CreateAsync(ProducerModel producer, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync), "producers.Producer");

        await using var command = _unitOfWork.CreateCommand(INSERT_PRODUCER_SQL);

        command.Parameters.Add("@id_producer", SqlDbType.UniqueIdentifier).Value = producer.Id;
        command.Parameters.Add("@name", SqlDbType.NVarChar, NAME_MAX_LENGTH).Value = producer.Name;
        command.Parameters.Add("@email", SqlDbType.NVarChar, EMAIL_MAX_LENGTH).Value = producer.Email;
        command.Parameters.Add("@phone", SqlDbType.VarChar, PHONE_MAX_LENGTH).Value = producer.Phone;
        command.Parameters.Add("@document", SqlDbType.VarChar, DOCUMENT_MAX_LENGTH).Value = (object?)producer.Document ?? DBNull.Value;
        command.Parameters.Add("@slug", SqlDbType.VarChar, SLUG_MAX_LENGTH).Value = producer.Slug;
        command.Parameters.Add("@status", SqlDbType.VarChar, STATUS_MAX_LENGTH).Value = producer.Status;
        command.Parameters.Add("@created_at", SqlDbType.DateTime2).Value = producer.CreatedAt;
        command.Parameters.Add("@created_correlation_id", SqlDbType.UniqueIdentifier).Value = producer.CreatedCorrelationId;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);

            return ProducerCreationResult.Created;
        }
        catch (SqlException ex) when (ex.Number == UNIQUE_INDEX_VIOLATION_ERROR_NUMBER)
        {
            if (ex.Message.Contains(UNIQUE_SLUG_INDEX_NAME))
                return ProducerCreationResult.SlugAlreadyExists;

            return ex.Message.Contains(UNIQUE_DOCUMENT_INDEX_NAME)
                ? ProducerCreationResult.DocumentAlreadyExists
                : ProducerCreationResult.EmailAlreadyExists;
        }
    }

    public async Task<ProducerUserCreationResult> CreateUserAsync(ProducerUserModel user, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateUserAsync), "producers.ProducerUser");

        await using var command = _unitOfWork.CreateCommand(INSERT_USER_SQL);

        command.Parameters.Add("@id_producer_user", SqlDbType.UniqueIdentifier).Value = user.Id;
        command.Parameters.Add("@producer_id", SqlDbType.UniqueIdentifier).Value = user.ProducerId;
        command.Parameters.Add("@name", SqlDbType.NVarChar, NAME_MAX_LENGTH).Value = user.Name;
        command.Parameters.Add("@email", SqlDbType.NVarChar, EMAIL_MAX_LENGTH).Value = user.Email;
        command.Parameters.Add("@password_hash", SqlDbType.Binary, PASSWORD_HASH_LENGTH).Value = user.PasswordHash;
        command.Parameters.Add("@password_salt", SqlDbType.Binary, PASSWORD_SALT_LENGTH).Value = user.PasswordSalt;
        command.Parameters.Add("@user_type", SqlDbType.VarChar, USER_TYPE_MAX_LENGTH).Value = user.UserType;
        command.Parameters.Add("@status", SqlDbType.VarChar, STATUS_MAX_LENGTH).Value = user.Status;
        command.Parameters.Add("@created_at", SqlDbType.DateTime2).Value = user.CreatedAt;
        command.Parameters.Add("@created_correlation_id", SqlDbType.UniqueIdentifier).Value = user.CreatedCorrelationId;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);

            return ProducerUserCreationResult.Created;
        }
        catch (SqlException ex) when (ex.Number == UNIQUE_INDEX_VIOLATION_ERROR_NUMBER)
        {
            return ProducerUserCreationResult.EmailAlreadyExists;
        }
    }

    public async Task CreateEmailVerificationAsync(EmailVerificationModel emailVerification, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateEmailVerificationAsync), "producers.EmailVerification");

        await using var command = _unitOfWork.CreateCommand(INSERT_EMAIL_VERIFICATION_SQL);

        command.Parameters.Add("@id_email_verification", SqlDbType.UniqueIdentifier).Value = emailVerification.Id;
        command.Parameters.Add("@producer_user_id", SqlDbType.UniqueIdentifier).Value = emailVerification.ProducerUserId;
        command.Parameters.Add("@token_hash", SqlDbType.Binary, TOKEN_HASH_LENGTH).Value = emailVerification.TokenHash;
        command.Parameters.Add("@expires_at", SqlDbType.DateTime2).Value = emailVerification.ExpiresAt;
        command.Parameters.Add("@created_at", SqlDbType.DateTime2).Value = emailVerification.CreatedAt;
        command.Parameters.Add("@created_correlation_id", SqlDbType.UniqueIdentifier).Value = emailVerification.CreatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
