using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace TestSupport;

public sealed partial class SqlServerTestDatabase : IAsyncDisposable
{
    public const string CONNECTION_STRING_ENVIRONMENT_VARIABLE = "SqlServerConfiguration__ConnectionString";

    private readonly string _serverConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private SqlServerTestDatabase(string serverConnectionString, string databaseName)
    {
        _serverConnectionString = serverConnectionString;
        DatabaseName = databaseName;
        ConnectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = databaseName }.ConnectionString;
    }

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(CONNECTION_STRING_ENVIRONMENT_VARIABLE)
            ?? throw new InvalidOperationException($"Set the '{CONNECTION_STRING_ENVIRONMENT_VARIABLE}' environment variable. See docs/README.md.");

        var serverConnectionString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        var database = new SqlServerTestDatabase(serverConnectionString, $"ntix_tests_{Guid.NewGuid():N}");

        await using (var connection = new SqlConnection(serverConnectionString))
        {
            await connection.OpenAsync();

            await using var command = new SqlCommand($"CREATE DATABASE [{database.DatabaseName}]", connection);
            await command.ExecuteNonQueryAsync();
        }

        await database.ApplyScriptsAsync();

        return database;
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    public async Task<int> ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddRange(parameters);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];",
            connection);

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
            foreach (var batch in GoSeparator().Split(await File.ReadAllTextAsync(script)))
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;

                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
