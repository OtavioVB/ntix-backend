using Npgsql;
using NpgsqlTypes;
using System.Security.Cryptography;

namespace IntegratedTests.Support;

/// <summary>Produtoras e fornecedores únicos por teste, gravados direto no banco, para os testes não dependerem da ordem.</summary>
public static class PaymentIntegratedTestData
{
    public static readonly string ENCRYPTION_KEY = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public const string ABACATEPAY = "ABACATEPAY";
    public const string API_KEY = "API_KEY";
    public const string ACCOUNT_ID = "ACCOUNT_ID";

    private const string INSERT_PRODUCER_SQL = """
        INSERT INTO producers.producer (id_producer, name, email, phone, slug, status, created_at, created_correlation_id)
        VALUES (@id_producer, @name, @email, '+5511987654321', @slug, 'PENDING', now(), @id_producer);
        """;

    private const string INSERT_PROVIDER_SQL = """
        INSERT INTO payments.provider (id_provider, code, name, status) VALUES (@id_provider, @code, @code, 'ENABLED');
        """;

    private const string INSERT_PROVIDER_METHOD_SQL = """
        INSERT INTO payments.provider_payment_method (id_provider, payment_method) VALUES (@id_provider, @payment_method);
        """;

    private const string INSERT_PROVIDER_KEY_SQL = """
        INSERT INTO payments.provider_credential_key (id_provider, credential_key, is_secret, is_required) VALUES (@id_provider, @credential_key, @is_secret, TRUE);
        """;

    /// <returns>Id e slug da produtora criada.</returns>
    public static async Task<(Guid Id, string Slug)> InsertProducerAsync(PostgreSqlFixture fixture)
    {
        var id = Guid.NewGuid();
        var slug = $"p-{id:N}";

        await fixture.ExecuteAsync(
            INSERT_PRODUCER_SQL,
            new NpgsqlParameter("@id_producer", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@name", NpgsqlDbType.Varchar) { Value = $"Produtora {id:N}" },
            new NpgsqlParameter("@email", NpgsqlDbType.Varchar) { Value = $"{id:N}@produtora.com" },
            new NpgsqlParameter("@slug", NpgsqlDbType.Varchar) { Value = slug });

        return (id, slug);
    }

    /// <summary>Fornecedor de teste com uma chave secreta (API_KEY) e uma não secreta (ACCOUNT_ID), ambas obrigatórias.</summary>
    /// <returns>Código do fornecedor criado.</returns>
    public static async Task<string> InsertProviderAsync(PostgreSqlFixture fixture, params string[] paymentMethods)
    {
        var id = Guid.NewGuid();
        var code = $"T_{id:N}"[..32];

        await fixture.ExecuteAsync(
            INSERT_PROVIDER_SQL,
            new NpgsqlParameter("@id_provider", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = code });

        foreach (var paymentMethod in paymentMethods)
        {
            await fixture.ExecuteAsync(
                INSERT_PROVIDER_METHOD_SQL,
                new NpgsqlParameter("@id_provider", NpgsqlDbType.Uuid) { Value = id },
                new NpgsqlParameter("@payment_method", NpgsqlDbType.Varchar) { Value = paymentMethod });
        }

        foreach (var (key, isSecret) in new[] { (API_KEY, true), (ACCOUNT_ID, false) })
        {
            await fixture.ExecuteAsync(
                INSERT_PROVIDER_KEY_SQL,
                new NpgsqlParameter("@id_provider", NpgsqlDbType.Uuid) { Value = id },
                new NpgsqlParameter("@credential_key", NpgsqlDbType.Varchar) { Value = key },
                new NpgsqlParameter("@is_secret", NpgsqlDbType.Boolean) { Value = isSecret });
        }

        return code;
    }
}
