using Npgsql;

namespace TestSupport;

public sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    public const string CONNECTION_STRING_ENVIRONMENT_VARIABLE = "PostgreSqlConfiguration__ConnectionString";

    private const string MAINTENANCE_DATABASE = "postgres";

    private readonly string _serverConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private PostgreSqlTestDatabase(string serverConnectionString, string databaseName)
    {
        _serverConnectionString = serverConnectionString;
        DatabaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
    }

    public static async Task<PostgreSqlTestDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(CONNECTION_STRING_ENVIRONMENT_VARIABLE)
            ?? throw new InvalidOperationException($"Set the '{CONNECTION_STRING_ENVIRONMENT_VARIABLE}' environment variable. See docs/README.md.");

        var serverConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Database = MAINTENANCE_DATABASE }.ConnectionString;
        var database = new PostgreSqlTestDatabase(serverConnectionString, $"ntix_tests_{Guid.NewGuid():N}");

        await using (var connection = new NpgsqlConnection(serverConnectionString))
        {
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database.DatabaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        await database.ApplyScriptsAsync();

        return database;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    public async Task<int> ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddRange(parameters);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)", connection);

        await command.ExecuteNonQueryAsync();
    }

    private async Task ApplyScriptsAsync()
    {
        var scripts = Directory
            .GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.sql", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

        await using var connection = await OpenConnectionAsync();

        foreach (var script in scripts)
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(script), connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
