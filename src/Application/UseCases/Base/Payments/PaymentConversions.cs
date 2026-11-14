using Application.UseCases.Base.Payments.Outputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using System.Text;

namespace Application.UseCases.Base.Payments;

/// <summary>
/// Conversão entre as entidades de pagamento e os modelos primitivos da Infra, compartilhada pelos casos de uso do
/// domínio de pagamentos. É também onde as credenciais são cifradas e decifradas, sempre amarradas à integração e à chave.
/// </summary>
internal static class PaymentConversions
{
    public static Provider ToProvider(PaymentProviderModel model)
        => new(
            id: IdValueObject.Build(model.Id.ToString()),
            code: model.Code,
            name: model.Name,
            status: Enum.Parse<ProviderStatus>(model.Status),
            paymentMethods: model.PaymentMethods.Select(Enum.Parse<PaymentMethod>).ToHashSet(),
            credentialKeys: model.CredentialKeys.Select(key => new ProviderCredentialKey(key.Key, key.IsSecret, key.IsRequired)).ToArray());

    public static IReadOnlyDictionary<Guid, Provider> ToProvidersById(IEnumerable<PaymentProviderModel> models)
        => models.Select(ToProvider).ToDictionary(provider => provider.Id.Value);

    public static ProducerIntegration ToIntegration(ProducerIntegrationModel model, IReadOnlyDictionary<Guid, Provider> providersById)
        => new(
            id: IdValueObject.Build(model.Id.ToString()),
            producerId: IdValueObject.Build(model.ProducerId.ToString()),
            provider: providersById[model.ProviderId],
            name: IntegrationNameValueObject.Build(model.Name),
            status: Enum.Parse<IntegrationStatus>(model.Status),
            credentialKeys: model.Credentials.Select(credential => credential.Key).ToHashSet(),
            createdAt: DateTimeValueObject.Build(model.CreatedAt),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            updatedAt: DateTimeValueObject.Build(model.UpdatedAt),
            updatedCorrelationId: IdValueObject.Build(model.UpdatedCorrelationId.ToString()));

    public static ProducerIntegrationModel ToModel(ProducerIntegration integration, IReadOnlyList<ProducerIntegrationCredentialModel> credentials)
        => new(
            Id: integration.Id.Value,
            ProducerId: integration.ProducerId.Value,
            ProviderId: integration.Provider.Id.Value,
            Name: integration.Name.Value,
            Status: integration.Status.ToString(),
            CreatedAt: integration.CreatedAt.Value,
            CreatedCorrelationId: integration.CreatedCorrelationId.Value,
            UpdatedAt: integration.UpdatedAt.Value,
            UpdatedCorrelationId: integration.UpdatedCorrelationId.Value,
            Credentials: credentials);

    /// <summary>Cifra o valor (RN9) e guarda o <c>value_hint</c> só de chave secreta (RN10).</summary>
    public static ProducerIntegrationCredentialModel EncryptCredential(ICredentialCipher cipher, ProducerIntegration integration, string key, CredentialValueObject value)
    {
        var isSecret = integration.Provider.FindCredentialKey(key)?.IsSecret ?? true;

        return new ProducerIntegrationCredentialModel(
            Key: key,
            EncryptedValue: cipher.Encrypt(Encoding.UTF8.GetBytes(value.Value), GetAssociatedData(integration.Id.Value, key)),
            ValueHint: isSecret ? value.GetObfuscatedValue() : null,
            UpdatedAt: integration.UpdatedAt.Value,
            UpdatedCorrelationId: integration.UpdatedCorrelationId.Value);
    }

    public static string DecryptCredential(ICredentialCipher cipher, Guid integrationId, ProducerIntegrationCredentialModel credential)
        => Encoding.UTF8.GetString(cipher.Decrypt(credential.EncryptedValue, GetAssociatedData(integrationId, credential.Key)));

    // O valor cifrado de uma integração e chave não decifra em outra linha: copiar o encrypted_value no banco não troca a conta.
    private static byte[] GetAssociatedData(Guid integrationId, string key)
        => Encoding.UTF8.GetBytes($"{integrationId:N}:{key}");

    /// <summary>
    /// Saída da integração. Só a chave não secreta é decifrada, para ser exibida; a secreta sai pelo <c>value_hint</c>
    /// gravado, sem decifrar (RN9, RN10).
    /// </summary>
    public static ProducerIntegrationOutput ToOutput(ProducerIntegration integration, IReadOnlyList<ProducerIntegrationCredentialModel> credentials, ICredentialCipher cipher)
        => new(
            Id: integration.Id.Value,
            ProviderCode: integration.Provider.Code,
            Name: integration.Name.Value,
            Status: integration.Status.ToString(),
            Credentials: credentials
                .OrderBy(credential => credential.Key, StringComparer.Ordinal)
                .Select(credential =>
                {
                    var isSecret = integration.Provider.FindCredentialKey(credential.Key)?.IsSecret ?? true;

                    return new ProducerIntegrationCredentialOutput(
                        Key: credential.Key,
                        IsSecret: isSecret,
                        ValueHint: isSecret ? credential.ValueHint : null,
                        Value: isSecret ? null : DecryptCredential(cipher, integration.Id.Value, credential));
                })
                .ToArray());

    /// <summary>Monta a rota do método a partir das posições gravadas, já em ordem de prioridade.</summary>
    public static PaymentRoute ToRoute(Guid producerId, PaymentMethod paymentMethod, IEnumerable<PaymentRouteModel> routes, IReadOnlyDictionary<Guid, ProducerIntegration> integrationsById)
        => new(
            producerId: IdValueObject.Build(producerId.ToString()),
            paymentMethod: paymentMethod,
            integrations: routes
                .Where(route => route.PaymentMethod == paymentMethod.ToString())
                .OrderBy(route => route.Priority)
                .Select(route => integrationsById[route.IntegrationId])
                .ToArray());

    public static IReadOnlyList<PaymentRouteModel> ToModels(PaymentRoute route, DateTimeValueObject now, IdValueObject correlationId)
        => route.Integrations
            .Select((integration, index) => new PaymentRouteModel(
                Id: Guid.NewGuid(),
                ProducerId: route.ProducerId.Value,
                PaymentMethod: route.PaymentMethod.ToString(),
                Priority: checked((short)PaymentRoute.GetPriority(index)),
                IntegrationId: integration.Id.Value,
                UpdatedAt: now.Value,
                UpdatedCorrelationId: correlationId.Value))
            .ToArray();

    public static IReadOnlyList<PaymentRouteItemOutput> ToOutput(PaymentRoute route)
        => route.Integrations
            .Select((integration, index) => new PaymentRouteItemOutput(
                Priority: PaymentRoute.GetPriority(index),
                IntegrationId: integration.Id.Value,
                Name: integration.Name.Value,
                Status: integration.Status.ToString()))
            .ToArray();
}
