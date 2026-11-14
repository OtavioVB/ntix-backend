using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.BoundedContexts.PaymentContext.Services;

/// <summary>
/// Decide por quais integrações cobrar um método (RN8): as da rota, em ordem de prioridade, que podem processar o
/// método agora. Não chama o fornecedor (RN12): a indisponibilidade na cobrança é tratada pelo fallback do REQ7.
/// </summary>
public static class PaymentIntegrationRanking
{
    public const string PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE = "PAYMENT_METHOD_UNAVAILABLE";
    private const string PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_MESSAGE = "O método de pagamento não está disponível para este produtor.";

    /// <summary>Lista ordenada: a primeira é a principal e as demais são fallback. Lista vazia vira PAYMENT_METHOD_UNAVAILABLE.</summary>
    public static MethodResult<IReadOnlyList<ProducerIntegration>> Rank(PaymentRoute route)
    {
        var ranked = route.Integrations
            .Where(integration => integration.CanProcess(route.PaymentMethod))
            .ToArray();

        if (ranked.Length == 0)
        {
            var notification = Notification.Error(
                code: PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE,
                message: PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_MESSAGE);

            return MethodResult<IReadOnlyList<ProducerIntegration>>.Error([notification]);
        }

        return MethodResult<IReadOnlyList<ProducerIntegration>>.Success(ranked);
    }
}
