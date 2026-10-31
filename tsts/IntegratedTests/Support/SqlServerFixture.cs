using Application.UseCases;
using Infra.SqlServer.Base.UnitOfWork;
using Infra.SqlServer.Repositories.Customers;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using TestSupport;

namespace IntegratedTests.Support;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string SELECT_CUSTOMER_BY_EMAIL_SQL = """
        SELECT id_customer, full_name, email, document, slug, password_hash, password_salt, phone_number, created_at, created_correlation_id
        FROM customers.Customer
        WHERE email = @email;
        """;

    private SqlServerTestDatabase? _database;
    private ServiceProvider? _serviceProvider;

    public IServiceProvider Services => _serviceProvider!;

    public async Task InitializeAsync()
    {
        _database = await SqlServerTestDatabase.CreateAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlServerConfiguration:ConnectionString"] = _database.ConnectionString,
                ["PasswordConfiguration:PrivateKey"] = CustomerTestData.PRIVATE_KEY,
            })
            .Build();

        _serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddSqlServerConfiguration(configuration)
            .AddCustomerRepositoryConfiguration(configuration)
            .AddUseCasesConfiguration(configuration)
            .BuildServiceProvider();
    }

    public async Task<CustomerModel?> FindCustomerByEmailAsync(string email)
    {
        await using var connection = await _database!.OpenConnectionAsync();
        await using var command = new SqlCommand(SELECT_CUSTOMER_BY_EMAIL_SQL, connection);

        command.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new CustomerModel(
            Id: reader.GetGuid(0),
            FullName: reader.GetString(1),
            Email: reader.GetString(2),
            Document: reader.GetString(3),
            Slug: reader.GetString(4),
            PasswordHash: reader.GetFieldValue<byte[]>(5),
            PasswordSalt: reader.GetFieldValue<byte[]>(6),
            PhoneNumber: await reader.IsDBNullAsync(7) ? null : reader.GetString(7),
            CreatedAt: reader.GetDateTime(8),
            CreatedCorrelationId: reader.GetGuid(9));
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        if (_database is not null)
            await _database.DisposeAsync();
    }
}
