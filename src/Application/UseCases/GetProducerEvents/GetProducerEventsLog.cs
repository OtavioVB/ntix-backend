using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetProducerEvents;

internal static partial class GetProducerEventsLog
{
    [LoggerMessage(EventId = 9131, Level = LogLevel.Information,
        Message = "Eventos da produtora {ProducerSlug} listados com o filtro {Status}: {Count} evento(s).")]
    public static partial void Served(ILogger logger, string producerSlug, string status, int count);

    [LoggerMessage(EventId = 9132, Level = LogLevel.Warning,
        Message = "Lista de eventos recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 9133, Level = LogLevel.Warning,
        Message = "Lista de eventos recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9134, Level = LogLevel.Error,
        Message = "Falha inesperada ao listar os eventos.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9135, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da lista de eventos.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
