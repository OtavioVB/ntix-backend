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

    public static readonly string PAYMENT_CREDENTIAL_ENCRYPTION_KEY = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>Chave de assinatura do access token do usuário da produtora (REQ5) na API sob teste.</summary>
    public static readonly string JWT_SIGNING_KEY = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>Pasta de whitelabel da API sob teste: cada cenário grava nela os arquivos de que precisa.</summary>
    public static string WhitelabelDirectory { get; } = Path.Combine(Path.GetTempPath(), "ntix-functional-whitelabel-" + Guid.NewGuid().ToString("N"));

    [BeforeTestRun]
    public static async Task StartApi()
    {
        Database = await PostgreSqlTestDatabase.CreateAsync();

        Environment.SetEnvironmentVariable(PostgreSqlTestDatabase.CONNECTION_STRING_ENVIRONMENT_VARIABLE, Database.ConnectionString);
        Environment.SetEnvironmentVariable("PasswordConfiguration__PrivateKey", "chave-de-teste");
        Environment.SetEnvironmentVariable("PaymentCredentialConfiguration__EncryptionKey", PAYMENT_CREDENTIAL_ENCRYPTION_KEY);
        Environment.SetEnvironmentVariable("JwtConfiguration__SigningKey", JWT_SIGNING_KEY);
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
            DELETE FROM orders.order_item;
            DELETE FROM orders.orders;
            DELETE FROM customers.producer_customer;
            DELETE FROM customers.customer_session;
            DELETE FROM customers.customer;
            DELETE FROM events.ticket_batch;
            DELETE FROM events.event;
            DELETE FROM outbox.outbox_event;
            DELETE FROM payments.producer_payment_route;
            DELETE FROM payments.producer_integration_credential;
            DELETE FROM payments.producer_integration;
            DELETE FROM payments.provider_payment_method WHERE id_provider IN (SELECT id_provider FROM payments.provider WHERE code <> 'ABACATEPAY');
            DELETE FROM payments.provider_credential_key WHERE id_provider IN (SELECT id_provider FROM payments.provider WHERE code <> 'ABACATEPAY');
            DELETE FROM payments.provider WHERE code <> 'ABACATEPAY';
            UPDATE payments.provider SET status = 'ENABLED';
            DELETE FROM producers.producer_user_session;
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
