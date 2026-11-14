using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.PaymentContext.Entities;

/// <summary>
/// Lista ordenada das integrações do produtor para um método de pagamento. A posição é a prioridade: a primeira é a
/// principal e as demais são fallback (REQ7).
/// </summary>
public sealed record PaymentRoute
{
    public const int FIRST_PRIORITY = 1;

    public PaymentRoute(IdValueObject producerId, PaymentMethod paymentMethod, IReadOnlyList<ProducerIntegration> integrations)
    {
        ProducerId = producerId;
        PaymentMethod = paymentMethod;
        Integrations = integrations;
    }

    public IdValueObject ProducerId { get; }
    public PaymentMethod PaymentMethod { get; }

    /// <summary>Integrações em ordem de prioridade, inclusive as inativas, que continuam na rota (RN11).</summary>
    public IReadOnlyList<ProducerIntegration> Integrations { get; }

    /// <summary>Prioridade da integração na posição informada: 1 em diante, sem buracos (RN6).</summary>
    public static int GetPriority(int index)
        => index + FIRST_PRIORITY;

    private const string INTEGRATION_NOT_FOUND_NOTIFICATION_CODE = "INTEGRATION_NOT_FOUND";
    private const string INTEGRATION_NOT_FOUND_NOTIFICATION_MESSAGE = "Uma das integrações informadas não existe para o produtor.";

    private const string INTEGRATION_DUPLICATED_IN_ROUTE_NOTIFICATION_CODE = "INTEGRATION_DUPLICATED_IN_ROUTE";
    private const string INTEGRATION_DUPLICATED_IN_ROUTE_NOTIFICATION_MESSAGE = "Uma integração só pode aparecer uma vez no mesmo método de pagamento.";

    private const string PROVIDER_DOES_NOT_SUPPORT_METHOD_NOTIFICATION_CODE = "PROVIDER_DOES_NOT_SUPPORT_METHOD";
    private const string PROVIDER_DOES_NOT_SUPPORT_METHOD_NOTIFICATION_MESSAGE = "O fornecedor de uma das integrações não aceita este método de pagamento.";

    /// <summary>
    /// Monta a nova lista do método a partir dos ids na ordem de prioridade. Só aceita integrações do próprio produtor
    /// (RN1), cada uma uma vez (RN6), de fornecedores que aceitam o método (RN7). Lista vazia remove o método.
    /// </summary>
    public static MethodResult<PaymentRoute> Build(
        IdValueObject producerId,
        PaymentMethod paymentMethod,
        IReadOnlyList<string?> integrationIds,
        IReadOnlyCollection<ProducerIntegration> producerIntegrations)
    {
        var notifications = new List<INotification>(capacity: 3);
        var producerIntegrationsById = producerIntegrations.ToDictionary(integration => integration.Id.Value);
        var seenIds = new HashSet<Guid>();
        var integrations = new List<ProducerIntegration>(integrationIds.Count);

        bool hasNotFound = false, hasDuplicated = false, hasUnsupported = false;

        foreach (var integrationId in integrationIds)
        {
            // Id fora do formato ou de outro produtor recebe a mesma resposta: não revela integrações de terceiros.
            if (!Guid.TryParse(integrationId, out var id) || !producerIntegrationsById.TryGetValue(id, out var integration))
            {
                hasNotFound = true;
                continue;
            }

            if (!seenIds.Add(id))
            {
                hasDuplicated = true;
                continue;
            }

            if (!integration.Provider.Supports(paymentMethod))
            {
                hasUnsupported = true;
                continue;
            }

            integrations.Add(integration);
        }

        if (hasNotFound)
            notifications.Add(Notification.Error(INTEGRATION_NOT_FOUND_NOTIFICATION_CODE, INTEGRATION_NOT_FOUND_NOTIFICATION_MESSAGE));

        if (hasDuplicated)
            notifications.Add(Notification.Error(INTEGRATION_DUPLICATED_IN_ROUTE_NOTIFICATION_CODE, INTEGRATION_DUPLICATED_IN_ROUTE_NOTIFICATION_MESSAGE));

        if (hasUnsupported)
            notifications.Add(Notification.Error(PROVIDER_DOES_NOT_SUPPORT_METHOD_NOTIFICATION_CODE, PROVIDER_DOES_NOT_SUPPORT_METHOD_NOTIFICATION_MESSAGE));

        if (notifications.HasAnyNotifications())
            return MethodResult<PaymentRoute>.Error(notifications.ToArray());

        return MethodResult<PaymentRoute>.Success(new PaymentRoute(producerId, paymentMethod, integrations));
    }
}
