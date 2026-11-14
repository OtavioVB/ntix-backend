using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetPaymentRoutes;

internal static partial class GetPaymentRoutesLog
{
    [LoggerMessage(EventId = 4501, Level = LogLevel.Information,
        Message = "Rotas de pagamento da produtora {ProducerSlug} consultadas: {Routes}.")]
    public static partial void Served(ILogger logger, string producerSlug, string routes);

    [LoggerMessage(EventId = 4502, Level = LogLevel.Warning,
        Message = "Consulta das rotas de pagamento recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 4503, Level = LogLevel.Warning,
        Message = "Consulta das rotas de pagamento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4504, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar as rotas de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4505, Level = LogLevel.Error,
        Message = "Falha ao encerrar a transação de leitura das rotas de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
