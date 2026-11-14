using Microsoft.Extensions.Logging;

namespace Application.UseCases.RankPaymentIntegrations;

internal static partial class RankPaymentIntegrationsLog
{
    [LoggerMessage(EventId = 4701, Level = LogLevel.Information,
        Message = "Ranqueamento de {PaymentMethod} da produtora {ProducerId}: [{Integrations}].")]
    public static partial void Ranked(ILogger logger, string paymentMethod, Guid producerId, string integrations);

    [LoggerMessage(EventId = 4702, Level = LogLevel.Warning,
        Message = "Ranqueamento de {PaymentMethod} da produtora {ProducerId} vazio: {RouteCount} integrações na rota, nenhuma disponível. O método não é oferecido no checkout.")]
    public static partial void Unavailable(ILogger logger, string paymentMethod, Guid producerId, int routeCount);

    [LoggerMessage(EventId = 4703, Level = LogLevel.Warning,
        Message = "Ranqueamento recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 4704, Level = LogLevel.Error,
        Message = "Falha inesperada ao ranquear as integrações de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4705, Level = LogLevel.Error,
        Message = "Falha ao encerrar a transação de leitura do ranqueamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
