using Infra.Resend.Base.EmailSender.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using TestSupport;
using WebApi;

namespace FunctionalTests.Support;

[Binding]
public sealed class NtixApiHooks
{
    public static WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public static SqlServerTestDatabase Database { get; private set; } = null!;
    public static FakeEmailSender EmailSender { get; private set; } = new();

    [BeforeTestRun]
    public static async Task StartApi()
    {
        Database = await SqlServerTestDatabase.CreateAsync();

        Environment.SetEnvironmentVariable(SqlServerTestDatabase.CONNECTION_STRING_ENVIRONMENT_VARIABLE, Database.ConnectionString);
        Environment.SetEnvironmentVariable("PasswordConfiguration__PrivateKey", "chave-de-teste");
        Environment.SetEnvironmentVariable("JwtConfiguration__SecretKey", "chave-jwt-de-teste-com-mais-de-32-bytes");
        Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", bool.TrueString);

        Environment.SetEnvironmentVariable("ResendConfiguration__ApiKey", "chave-de-teste");
        Environment.SetEnvironmentVariable("ResendConfiguration__From", "Ntix <nao-responda@ntix.com>");
        Environment.SetEnvironmentVariable("EmailVerificationConfiguration__LinkBaseUrl", "https://painel.ntix.com/verificar-email");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddTransient<IEmailSender>(_ => EmailSender)));
    }

    [AfterTestRun]
    public static async Task StopApi()
    {
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
    }

    [BeforeScenario]
    public Task RemoveCustomersFromPreviousScenarios()
    {
        EmailSender = new FakeEmailSender();

        return Database.ExecuteAsync("DELETE FROM producers.EmailVerification; DELETE FROM producers.ProducerUser; DELETE FROM producers.ProducerCustomer; DELETE FROM producers.Producer; DELETE FROM customers.Customer;");
    }
}
