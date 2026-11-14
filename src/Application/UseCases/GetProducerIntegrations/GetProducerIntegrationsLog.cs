using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetProducerIntegrations;

internal static partial class GetProducerIntegrationsLog
{
    [LoggerMessage(EventId = 4301, Level = LogLevel.Information,
        Message = "Integrações de pagamento da produtora {ProducerSlug} consultadas: {IntegrationCount} integrações.")]
    public static partial void Served(ILogger logger, string producerSlug, int integrationCount);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning,
        Message = "Consulta das integrações de pagamento recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Warning,
        Message = "Consulta das integrações de pagamento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4304, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar as integrações de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4305, Level = LogLevel.Error,
        Message = "Falha ao encerrar a transação de leitura das integrações de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
