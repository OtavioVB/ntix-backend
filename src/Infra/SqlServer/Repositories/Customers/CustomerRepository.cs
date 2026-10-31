using Infra.SqlServer.Base.UnitOfWork;
using Infra.SqlServer.Repositories.Customers.Enums;
using Infra.SqlServer.Repositories.Customers.Interfaces;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;

namespace Infra.SqlServer.Repositories.Customers;

public sealed class CustomerRepository : ICustomerRepository
{
    private const int UNIQUE_INDEX_VIOLATION_ERROR_NUMBER = 2601;
    private const string UNIQUE_SLUG_INDEX_NAME = "UX_Customer_Slug";

    private const int FULL_NAME_MAX_LENGTH = 256;
    private const int EMAIL_MAX_LENGTH = 256;
    private const int DOCUMENT_LENGTH = 11;
    private const int SLUG_MAX_LENGTH = 262;

    private const string EXISTS_BY_EMAIL_OR_DOCUMENT_SQL = """
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM customers.Customer WHERE email = @email)
              OR EXISTS (SELECT 1 FROM customers.Customer WHERE document = @document)
            THEN 1 ELSE 0
        END;
        """;

    private const string EXISTS_BY_SLUG_SQL = """
        SELECT CASE
            WHEN EXISTS (SELECT 1 FROM customers.Customer WHERE slug = @slug)
            THEN 1 ELSE 0
        END;
        """;

    private const string INSERT_SQL = """
        INSERT INTO customers.Customer (id_customer, full_name, email, document, slug, created_at, created_correlation_id)
        VALUES (@id_customer, @full_name, @email, @document, @slug, @created_at, @created_correlation_id);
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.SqlServer");

    private readonly SqlServerUnitOfWork _unitOfWork;

    public CustomerRepository(SqlServerUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual: nunca registra parâmetros nem a mensagem da SqlException, que traz o valor duplicado.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(CustomerRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "microsoft.sql.server")
            .SetTag("db.collection.name", "customers.Customer");

    public async Task<bool> ExistsByEmailOrDocumentAsync(string email, string document, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsByEmailOrDocumentAsync));

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_EMAIL_OR_DOCUMENT_SQL);

        command.Parameters.Add("@email", SqlDbType.NVarChar, EMAIL_MAX_LENGTH).Value = email;
        command.Parameters.Add("@document", SqlDbType.Char, DOCUMENT_LENGTH).Value = document;

        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsBySlugAsync));

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_SLUG_SQL);

        command.Parameters.Add("@slug", SqlDbType.VarChar, SLUG_MAX_LENGTH).Value = slug;

        return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
    }

    public async Task<CustomerCreationResult> CreateAsync(CustomerModel customer, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync));

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_customer", SqlDbType.UniqueIdentifier).Value = customer.Id;
        command.Parameters.Add("@full_name", SqlDbType.NVarChar, FULL_NAME_MAX_LENGTH).Value = customer.FullName;
        command.Parameters.Add("@email", SqlDbType.NVarChar, EMAIL_MAX_LENGTH).Value = customer.Email;
        command.Parameters.Add("@document", SqlDbType.Char, DOCUMENT_LENGTH).Value = customer.Document;
        command.Parameters.Add("@slug", SqlDbType.VarChar, SLUG_MAX_LENGTH).Value = customer.Slug;
        command.Parameters.Add("@created_at", SqlDbType.DateTime2).Value = customer.CreatedAt;
        command.Parameters.Add("@created_correlation_id", SqlDbType.UniqueIdentifier).Value = customer.CreatedCorrelationId;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);

            return CustomerCreationResult.Created;
        }
        catch (SqlException ex) when (ex.Number == UNIQUE_INDEX_VIOLATION_ERROR_NUMBER)
        {
            return ex.Message.Contains(UNIQUE_SLUG_INDEX_NAME)
                ? CustomerCreationResult.SlugAlreadyExists
                : CustomerCreationResult.EmailOrDocumentAlreadyExists;
        }
    }
}
