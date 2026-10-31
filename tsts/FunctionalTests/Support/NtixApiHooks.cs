using Microsoft.AspNetCore.Mvc.Testing;
using Reqnroll;
using TestSupport;
using WebApi;

namespace FunctionalTests.Support;

[Binding]
public sealed class NtixApiHooks
{
    public static WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public static SqlServerTestDatabase Database { get; private set; } = null!;

    [BeforeTestRun]
    public static async Task StartApi()
    {
        Database = await SqlServerTestDatabase.CreateAsync();

        Environment.SetEnvironmentVariable(SqlServerTestDatabase.CONNECTION_STRING_ENVIRONMENT_VARIABLE, Database.ConnectionString);
        Environment.SetEnvironmentVariable("PasswordConfiguration__PrivateKey", "chave-de-teste");
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", bool.TrueString);

        Factory = new WebApplicationFactory<Program>();
    }

    [AfterTestRun]
    public static async Task StopApi()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }

    [BeforeScenario]
    public Task RemoveCustomersFromPreviousScenarios()
        => Database.ExecuteAsync("DELETE FROM customers.Customer;");
}
