using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Payments.Models;

namespace UnitTests.Support;

/// <summary>Catálogo e integrações de pagamento para os testes, no formato do seed do AbacatePay.</summary>
public static class PaymentTestData
{
    public const string ABACATEPAY = "ABACATEPAY";
    public const string API_KEY = "API_KEY";
    public const string WEBHOOK_SECRET = "WEBHOOK_SECRET";
    public const string ACCOUNT_ID = "ACCOUNT_ID";

    public const string API_KEY_VALUE = "abc_dev_chave-secreta-a91f";
    public const string API_KEY_HINT = "a91f";

    public static readonly Guid ABACATEPAY_ID = Guid.Parse("0b6e7c1e-5d0a-4a43-9b8e-2f6a3c1d9e01");

    public static readonly DateTimeValueObject NOW = DateTimeValueObject.Build(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));
    public static readonly IdValueObject CORRELATION_ID = IdValueObject.Build("b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8");
    public static readonly IdValueObject PRODUCER_ID = IdValueObject.Build("8e7f6a5b-4c3d-4e2f-9a1b-0c9d8e7f6a5b");

    /// <summary>AbacatePay como no seed: API_KEY secreta e obrigatória, WEBHOOK_SECRET secreto e opcional.</summary>
    public static Provider AbacatePay(ProviderStatus status = ProviderStatus.ENABLED)
        => new(
            id: IdValueObject.Build(ABACATEPAY_ID.ToString()),
            code: ABACATEPAY,
            name: "AbacatePay",
            status: status,
            paymentMethods: new HashSet<PaymentMethod> { PaymentMethod.PIX, PaymentMethod.BOLETO, PaymentMethod.CREDIT_CARD },
            credentialKeys: [new ProviderCredentialKey(API_KEY, IsSecret: true, IsRequired: true), new ProviderCredentialKey(WEBHOOK_SECRET, IsSecret: true, IsRequired: false)]);

    /// <summary>Fornecedor de teste que só aceita PIX e tem uma chave não secreta (ACCOUNT_ID).</summary>
    public static Provider PixOnlyProvider(ProviderStatus status = ProviderStatus.ENABLED)
        => new(
            id: IdValueObject.Build("5f0c1a2b-3d4e-4f5a-8b6c-7d8e9f0a1b2c"),
            code: "SO_PIX",
            name: "Só Pix",
            status: status,
            paymentMethods: new HashSet<PaymentMethod> { PaymentMethod.PIX },
            credentialKeys: [new ProviderCredentialKey(API_KEY, IsSecret: true, IsRequired: true), new ProviderCredentialKey(ACCOUNT_ID, IsSecret: false, IsRequired: true)]);

    public static ProducerIntegration Integration(
        Provider? provider = null,
        IntegrationStatus status = IntegrationStatus.ACTIVE,
        IReadOnlySet<string>? credentialKeys = null,
        string name = "AbacatePay - conta principal",
        IdValueObject? producerId = null)
        => new(
            id: IdValueObject.Build(),
            producerId: producerId ?? PRODUCER_ID,
            provider: provider ?? AbacatePay(),
            name: IntegrationNameValueObject.Build(name),
            status: status,
            credentialKeys: credentialKeys ?? new HashSet<string> { API_KEY },
            createdAt: NOW,
            createdCorrelationId: CORRELATION_ID,
            updatedAt: NOW,
            updatedCorrelationId: CORRELATION_ID);

    public static PaymentProviderModel AbacatePayModel(string status = "ENABLED")
        => new(
            Id: ABACATEPAY_ID,
            Code: ABACATEPAY,
            Name: "AbacatePay",
            Status: status,
            PaymentMethods: ["BOLETO", "CREDIT_CARD", "PIX"],
            CredentialKeys: [new ProviderCredentialKeyModel(API_KEY, true, true), new ProviderCredentialKeyModel(WEBHOOK_SECRET, true, false)]);

    public static ProducerIntegrationModel IntegrationModel(Guid? id = null, string status = "ACTIVE", string name = "AbacatePay - conta principal", params ProducerIntegrationCredentialModel[] credentials)
        => new(
            Id: id ?? Guid.NewGuid(),
            ProducerId: PRODUCER_ID.Value,
            ProviderId: ABACATEPAY_ID,
            Name: name,
            Status: status,
            CreatedAt: NOW.Value,
            CreatedCorrelationId: CORRELATION_ID.Value,
            UpdatedAt: NOW.Value,
            UpdatedCorrelationId: CORRELATION_ID.Value,
            Credentials: credentials.Length == 0 ? [Credential(API_KEY, API_KEY_HINT)] : credentials);

    public static ProducerIntegrationCredentialModel Credential(string key, string? hint = null, byte[]? encryptedValue = null)
        => new(key, encryptedValue ?? [1, 2, 3], hint, NOW.Value, CORRELATION_ID.Value);
}
