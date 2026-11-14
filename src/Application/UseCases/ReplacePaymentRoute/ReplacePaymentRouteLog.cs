using Microsoft.Extensions.Logging;

namespace Application.UseCases.ReplacePaymentRoute;

internal static partial class ReplacePaymentRouteLog
{
    [LoggerMessage(EventId = 4601, Level = LogLevel.Information,
        Message = "Rota de {PaymentMethod} da produtora {ProducerSlug} substituída, em ordem de prioridade: [{Integrations}].")]
    public static partial void Replaced(ILogger logger, string paymentMethod, string producerSlug, string integrations);

    [LoggerMessage(EventId = 4602, Level = LogLevel.Warning,
        Message = "Substituição de rota de pagamento recusada para a produtora {ProducerSlug} por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 4603, Level = LogLevel.Warning,
        Message = "Substituição de rota de pagamento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4604, Level = LogLevel.Error,
        Message = "Falha inesperada ao substituir uma rota de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4605, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da substituição de rota de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
