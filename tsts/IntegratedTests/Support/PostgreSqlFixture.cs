using Application.UseCases;
using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers;
using Infra.PostgreSql.Repositories.Customers.Models;
using Infra.PostgreSql.Repositories.Outbox;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Producers;
using Infra.PostgreSql.Repositories.Producers.Models;
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
            .AddProducerRepositoryConfiguration(configuration)
            .AddOutboxRepositoryConfiguration(configuration)
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

    private const string SELECT_PRODUCER_BY_EMAIL_SQL = """
        SELECT id_producer, name, email, phone, slug, status, created_at, created_correlation_id
        FROM producers.producer
        WHERE email = @email;
        """;

    private const string SELECT_PRODUCER_USER_BY_EMAIL_SQL = """
        SELECT id_producer_user, id_producer, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id
        FROM producers.producer_user
        WHERE email = @email;
        """;

    private const string SELECT_OUTBOX_EVENTS_BY_AGGREGATE_SQL = """
        SELECT id_outbox_event, event_type, aggregate_id, payload::text, created_at, created_correlation_id
        FROM outbox.outbox_event
        WHERE aggregate_id = @aggregate_id;
        """;

    public async Task<ProducerModel?> FindProducerByEmailAsync(string email)
    {
        await using var connection = await _database!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(SELECT_PRODUCER_BY_EMAIL_SQL, connection);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, 256).Value = email;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new ProducerModel(
            Id: reader.GetGuid(0),
            Name: reader.GetString(1),
            Email: reader.GetString(2),
            Phone: reader.GetString(3),
            Slug: reader.GetString(4),
            Status: reader.GetString(5),
            CreatedAt: reader.GetDateTime(6),
            CreatedCorrelationId: reader.GetGuid(7));
    }

    public async Task<ProducerUserModel?> FindProducerUserByEmailAsync(string email)
    {
        await using var connection = await _database!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(SELECT_PRODUCER_USER_BY_EMAIL_SQL, connection);

        command.Parameters.Add("@email", NpgsqlDbType.Varchar, 256).Value = email;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
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

    public async Task<IReadOnlyList<OutboxEventModel>> FindOutboxEventsByAggregateAsync(Guid aggregateId)
    {
        await using var connection = await _database!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(SELECT_OUTBOX_EVENTS_BY_AGGREGATE_SQL, connection);

        command.Parameters.Add("@aggregate_id", NpgsqlDbType.Uuid).Value = aggregateId;

        await using var reader = await command.ExecuteReaderAsync();

        var events = new List<OutboxEventModel>();

        while (await reader.ReadAsync())
        {
            events.Add(new OutboxEventModel(
                Id: reader.GetGuid(0),
                EventType: reader.GetString(1),
                AggregateId: reader.GetGuid(2),
                Payload: reader.GetString(3),
                CreatedAt: reader.GetDateTime(4),
                CreatedCorrelationId: reader.GetGuid(5)));
        }

        return events;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        if (_database is not null)
            await _database.DisposeAsync();
    }
}
