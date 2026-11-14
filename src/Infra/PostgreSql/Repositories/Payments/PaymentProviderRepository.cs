using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Payments;

public sealed class PaymentProviderRepository : IPaymentProviderRepository
{
    // O catálogo tem poucas linhas: métodos e chaves vêm como arrays na mesma consulta, sem uma ida ao banco por fornecedor.
    private const string SELECT_PROVIDERS_SQL = """
        SELECT p.id_provider,
               p.code,
               p.name,
               p.status,
               ARRAY(SELECT m.payment_method FROM payments.provider_payment_method m WHERE m.id_provider = p.id_provider ORDER BY m.payment_method) AS payment_methods,
               ARRAY(SELECT k.credential_key FROM payments.provider_credential_key k WHERE k.id_provider = p.id_provider ORDER BY k.is_required DESC, k.credential_key) AS credential_keys,
               ARRAY(SELECT k.is_secret FROM payments.provider_credential_key k WHERE k.id_provider = p.id_provider ORDER BY k.is_required DESC, k.credential_key) AS credential_keys_is_secret,
               ARRAY(SELECT k.is_required FROM payments.provider_credential_key k WHERE k.id_provider = p.id_provider ORDER BY k.is_required DESC, k.credential_key) AS credential_keys_is_required
        FROM payments.provider p
        ORDER BY p.code;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public PaymentProviderRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<PaymentProviderModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(PaymentProviderRepository)}.{nameof(GetAllAsync)}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "payments.provider");

        await using var command = _unitOfWork.CreateCommand(SELECT_PROVIDERS_SQL);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var providers = new List<PaymentProviderModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var keys = reader.GetFieldValue<string[]>(5);
            var isSecret = reader.GetFieldValue<bool[]>(6);
            var isRequired = reader.GetFieldValue<bool[]>(7);

            providers.Add(new PaymentProviderModel(
                Id: reader.GetGuid(0),
                Code: reader.GetString(1),
                Name: reader.GetString(2),
                Status: reader.GetString(3),
                PaymentMethods: reader.GetFieldValue<string[]>(4),
                CredentialKeys: keys.Select((key, index) => new ProviderCredentialKeyModel(key, isSecret[index], isRequired[index])).ToArray()));
        }

        return providers;
    }
}
