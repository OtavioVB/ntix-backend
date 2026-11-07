using Microsoft.AspNetCore.Mvc.Testing;
using Reqnroll;
using TestSupport;
using WebApi;

namespace FunctionalTests.Support;

[Binding]
public sealed class NtixApiHooks
{
    public static WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public static PostgreSqlTestDatabase Database { get; private set; } = null!;

    /// <summary>Pasta de whitelabel da API sob teste: cada cenário grava nela os arquivos de que precisa.</summary>
    public static string WhitelabelDirectory { get; } = Path.Combine(Path.GetTempPath(), "ntix-functional-whitelabel-" + Guid.NewGuid().ToString("N"));

    [BeforeTestRun]
    public static async Task StartApi()
    {
        Database = await PostgreSqlTestDatabase.CreateAsync();

        Environment.SetEnvironmentVariable(PostgreSqlTestDatabase.CONNECTION_STRING_ENVIRONMENT_VARIABLE, Database.ConnectionString);
        Environment.SetEnvironmentVariable("PasswordConfiguration__PrivateKey", "chave-de-teste");
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", bool.TrueString);

        Directory.CreateDirectory(WhitelabelDirectory);
        Environment.SetEnvironmentVariable("WhitelabelConfiguration__DirectoryPath", WhitelabelDirectory);

        Factory = new WebApplicationFactory<Program>();
    }

    [AfterTestRun]
    public static async Task StopApi()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();

        if (Directory.Exists(WhitelabelDirectory))
            Directory.Delete(WhitelabelDirectory, recursive: true);
    }

    [BeforeScenario]
    public Task RemoveDataFromPreviousScenarios()
        => Database.ExecuteAsync("""
            DELETE FROM customers.customer;
            DELETE FROM outbox.outbox_event;
            DELETE FROM producers.producer_user;
            DELETE FROM producers.producer;
            """);

    [BeforeScenario]
    public void RemoveWhitelabelFilesFromPreviousScenarios()
    {
        foreach (var file in Directory.EnumerateFiles(WhitelabelDirectory))
            File.Delete(file);
    }
}
