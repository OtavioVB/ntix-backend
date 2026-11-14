using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.PaymentContext.Entities;

/// <summary>
/// Conexão do produtor com um fornecedor, com as credenciais da conta dele. A entidade guarda só quais chaves estão
/// preenchidas: os valores ficam cifrados no banco e só são decifrados na cobrança (RN9).
/// </summary>
public sealed record ProducerIntegration
{
    public ProducerIntegration(
        IdValueObject id,
        IdValueObject producerId,
        Provider provider,
        IntegrationNameValueObject name,
        IntegrationStatus status,
        IReadOnlySet<string> credentialKeys,
        DateTimeValueObject createdAt,
        IdValueObject createdCorrelationId,
        DateTimeValueObject updatedAt,
        IdValueObject updatedCorrelationId)
    {
        Id = id;
        ProducerId = producerId;
        Provider = provider;
        Name = name;
        Status = status;
        CredentialKeys = credentialKeys;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        UpdatedAt = updatedAt;
        UpdatedCorrelationId = updatedCorrelationId;
    }

    public IdValueObject Id { get; }
    public IdValueObject ProducerId { get; }
    public Provider Provider { get; }
    public IntegrationNameValueObject Name { get; }
    public IntegrationStatus Status { get; }

    /// <summary>Chaves de credencial preenchidas, sem os valores.</summary>
    public IReadOnlySet<string> CredentialKeys { get; }

    public DateTimeValueObject CreatedAt { get; }
    public IdValueObject CreatedCorrelationId { get; }
    public DateTimeValueObject UpdatedAt { get; }
    public IdValueObject UpdatedCorrelationId { get; }

    public bool IsActive => Status == IntegrationStatus.ACTIVE;

    public bool HasRequiredCredentials
        => Provider.RequiredCredentialKeys.All(credentialKey => CredentialKeys.Contains(credentialKey.Key));

    /// <summary>
    /// Pode receber cobranças do método (RN8): integração ativa, fornecedor habilitado no catálogo, que aceita o método,
    /// e com todas as credenciais obrigatórias.
    /// </summary>
    public bool CanProcess(PaymentMethod paymentMethod)
        => IsActive && Provider.IsEnabled && Provider.Supports(paymentMethod) && HasRequiredCredentials;

    private const string CREDENTIAL_KEY_REQUIRED_NOTIFICATION_CODE = "CREDENTIAL_KEY_REQUIRED";
    private const string CREDENTIAL_KEY_REQUIRED_NOTIFICATION_MESSAGE = "A credencial {0} é obrigatória para este fornecedor.";

    private const string CREDENTIAL_KEY_UNKNOWN_NOTIFICATION_CODE = "CREDENTIAL_KEY_UNKNOWN";
    private const string CREDENTIAL_KEY_UNKNOWN_NOTIFICATION_MESSAGE = "Foi informada uma credencial que este fornecedor não usa.";

    /// <summary>Conecta o produtor ao fornecedor (RN4, RN5). A integração nasce ACTIVE.</summary>
    public static MethodResult<ProducerIntegration> Create(
        IdValueObject producerId,
        Provider provider,
        IntegrationNameValueObject name,
        IReadOnlyDictionary<string, CredentialValueObject?> credentials,
        DateTimeValueObject now,
        IdValueObject correlationId)
    {
        var notifications = new List<INotification>();

        ValidateCredentialChanges(provider, credentials, allowRemoval: false, notifications);

        var credentialKeys = credentials.Keys.ToHashSet();

        ValidateRequiredCredentials(provider, credentialKeys, notifications);

        if (notifications.HasAnyNotifications())
            return MethodResult<ProducerIntegration>.Error(notifications.ToArray());

        var integration = new ProducerIntegration(
            id: IdValueObject.Build(),
            producerId: producerId,
            provider: provider,
            name: name,
            status: IntegrationStatus.ACTIVE,
            credentialKeys: credentialKeys,
            createdAt: now,
            createdCorrelationId: correlationId,
            updatedAt: now,
            updatedCorrelationId: correlationId);

        return MethodResult<ProducerIntegration>.Success(integration);
    }

    /// <summary>
    /// Altera nome, situação e credenciais. As credenciais são parciais: só as chaves enviadas mudam, e chave com valor
    /// nulo é removida, se não for obrigatória. Devolve outra instância, sem alterar esta.
    /// </summary>
    public MethodResult<ProducerIntegration> Update(
        IntegrationNameValueObject? name,
        IntegrationStatus? status,
        IReadOnlyDictionary<string, CredentialValueObject?> credentialChanges,
        DateTimeValueObject now,
        IdValueObject correlationId)
    {
        var notifications = new List<INotification>();

        ValidateCredentialChanges(Provider, credentialChanges, allowRemoval: true, notifications);

        var credentialKeys = CredentialKeys.ToHashSet();

        foreach (var (key, value) in credentialChanges)
        {
            if (value is null)
                credentialKeys.Remove(key);
            else
                credentialKeys.Add(key);
        }

        ValidateRequiredCredentials(Provider, credentialKeys, notifications);

        if (notifications.HasAnyNotifications())
            return MethodResult<ProducerIntegration>.Error(notifications.ToArray());

        var integration = new ProducerIntegration(
            id: Id,
            producerId: ProducerId,
            provider: Provider,
            name: name ?? Name,
            status: status ?? Status,
            credentialKeys: credentialKeys,
            createdAt: CreatedAt,
            createdCorrelationId: CreatedCorrelationId,
            updatedAt: now,
            updatedCorrelationId: correlationId);

        return MethodResult<ProducerIntegration>.Success(integration);
    }

    // Toda chave precisa existir no catálogo do fornecedor; todo valor enviado precisa ser válido (RN4, RN5).
    private static void ValidateCredentialChanges(Provider provider, IReadOnlyDictionary<string, CredentialValueObject?> credentials, bool allowRemoval, List<INotification> notifications)
    {
        var hasUnknownKey = false;

        foreach (var (key, value) in credentials)
        {
            if (provider.FindCredentialKey(key) is null)
            {
                hasUnknownKey = true;
                continue;
            }

            if (value is null)
            {
                if (!allowRemoval)
                    notifications.AddRange(CredentialValueObject.Build(null).MethodResult.Notifications);

                continue;
            }

            if (!value.Value.IsValid)
                notifications.AddRange(value.Value.MethodResult.Notifications);
        }

        // A chave desconhecida vem de quem chama e não entra na mensagem: pode ser longa ou trazer um segredo colado no lugar errado.
        if (hasUnknownKey)
        {
            notifications.Add(Notification.Error(
                code: CREDENTIAL_KEY_UNKNOWN_NOTIFICATION_CODE,
                message: CREDENTIAL_KEY_UNKNOWN_NOTIFICATION_MESSAGE));
        }
    }

    private static void ValidateRequiredCredentials(Provider provider, IReadOnlySet<string> credentialKeys, List<INotification> notifications)
    {
        foreach (var requiredKey in provider.RequiredCredentialKeys.Where(credentialKey => !credentialKeys.Contains(credentialKey.Key)))
        {
            notifications.Add(Notification.Error(
                code: CREDENTIAL_KEY_REQUIRED_NOTIFICATION_CODE,
                message: string.Format(System.Globalization.CultureInfo.InvariantCulture, CREDENTIAL_KEY_REQUIRED_NOTIFICATION_MESSAGE, requiredKey.Key)));
        }
    }
}
