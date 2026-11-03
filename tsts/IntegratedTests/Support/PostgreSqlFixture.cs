using Application.UseCases;
using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers;
using Infra.PostgreSql.Repositories.Customers.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using TestSupport;

namespace IntegratedTests.Support;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string SELECT_CUSTOMER_BY_EMAIL_SQL = """
        SELECT id_customer, full_name, email, document, slug, password_hash, password_salt, phone_number, created_at, created_correlation_id
        FROM customers.customer
        WHERE email = @email;
        """;

    private PostgreSqlTestDatabase? _database;
    private ServiceProvider? _serviceProvider;

    public IServiceProvider Services => _serviceProvider!;

    public async Task InitializeAsync()
    {
        _database = await PostgreSqlTestDatabase.CreateAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostgreSqlConfiguration:ConnectionString"] = _database.ConnectionString,
                ["PasswordConfiguration:PrivateKey"] = CustomerTestData.PRIVATE_KEY,
            })
            .Build();

        _serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddPostgreSqlConfiguration(configuration)
            .AddCustomerRepositoryConfiguration(configuration)
            .AddUseCasesConfiguration(configuration)
            .BuildServiceProvider();
    }

    public async Task<CustomerModel?> FindCustomerByEmailAsync(string email)
    {
        await using var connection = await _database!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(SELECT_CUSTOMER_BY_EMAIL_SQL, connection);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, 256).Value = email;

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
