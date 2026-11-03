using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers.Enums;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Customers;

public sealed class CustomerRepository : ICustomerRepository
{
    private const string UNIQUE_SLUG_INDEX_NAME = "ux_customer_slug";
    private const string CREATE_CUSTOMER_SAVEPOINT_NAME = "create_customer";

    private const int FULL_NAME_MAX_LENGTH = 256;
    private const int EMAIL_MAX_LENGTH = 256;
    private const int DOCUMENT_LENGTH = 11;
    private const int SLUG_MAX_LENGTH = 262;
    private const int PHONE_NUMBER_LENGTH = 11;

    private const string EXISTS_BY_EMAIL_OR_DOCUMENT_SQL = """
        SELECT EXISTS (SELECT 1 FROM customers.customer WHERE email = @email)
            OR EXISTS (SELECT 1 FROM customers.customer WHERE document = @document);
        """;

    private const string EXISTS_BY_SLUG_SQL = """
        SELECT EXISTS (SELECT 1 FROM customers.customer WHERE slug = @slug);
        """;

    private const string INSERT_SQL = """
        INSERT INTO customers.customer (id_customer, full_name, email, document, slug, password_hash, password_salt, phone_number, created_at, created_correlation_id)
        VALUES (@id_customer, @full_name, @email, @document, @slug, @password_hash, @password_salt, @phone_number, @created_at, @created_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public CustomerRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros nem a mensagem da PostgresException, que traz o valor duplicado.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(CustomerRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "customers.customer");

    public async Task<bool> ExistsByEmailOrDocumentAsync(string email, string document, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsByEmailOrDocumentAsync));

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_EMAIL_OR_DOCUMENT_SQL);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = email;
        command.Parameters.Add("@document", NpgsqlDbType.Char, DOCUMENT_LENGTH).Value = document;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsBySlugAsync));

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_SLUG_SQL);

        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = slug;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<CustomerCreationResult> CreateAsync(CustomerModel customer, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync));

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_customer", NpgsqlDbType.Uuid).Value = customer.Id;
        command.Parameters.Add("@full_name", NpgsqlDbType.Varchar, FULL_NAME_MAX_LENGTH).Value = customer.FullName;
        command.Parameters.Add("@email", NpgsqlDbType.Varchar, EMAIL_MAX_LENGTH).Value = customer.Email;
        command.Parameters.Add("@document", NpgsqlDbType.Char, DOCUMENT_LENGTH).Value = customer.Document;
        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = customer.Slug;
        command.Parameters.Add("@password_hash", NpgsqlDbType.Bytea).Value = customer.PasswordHash;
        command.Parameters.Add("@password_salt", NpgsqlDbType.Bytea).Value = customer.PasswordSalt;
        command.Parameters.Add("@phone_number", NpgsqlDbType.Char, PHONE_NUMBER_LENGTH).Value = (object?)customer.PhoneNumber ?? DBNull.Value;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = customer.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = customer.CreatedCorrelationId;

        // A violação de índice invalidaria a transação; voltar ao savepoint mantém a nova tentativa na mesma transação.
        await _unitOfWork.SaveAsync(CREATE_CUSTOMER_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(CREATE_CUSTOMER_SAVEPOINT_NAME, cancellationToken);

            return ex.ConstraintName == UNIQUE_SLUG_INDEX_NAME
                ? CustomerCreationResult.SlugAlreadyExists
                : CustomerCreationResult.EmailOrDocumentAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(CREATE_CUSTOMER_SAVEPOINT_NAME, cancellationToken);

        return CustomerCreationResult.Created;
    }
}
