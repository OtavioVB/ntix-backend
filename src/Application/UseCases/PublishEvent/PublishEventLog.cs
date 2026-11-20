using Microsoft.Extensions.Logging;

namespace Application.UseCases.PublishEvent;

internal static partial class PublishEventLog
{
    [LoggerMessage(EventId = 10131, Level = LogLevel.Information,
        Message = "Evento {EventId} ({EventSlug}) da produtora {ProducerSlug} publicado com {BatchCount} lote(s).")]
    public static partial void Published(ILogger logger, Guid eventId, string eventSlug, string producerSlug, int batchCount);

    [LoggerMessage(EventId = 10132, Level = LogLevel.Warning,
        Message = "Publicação de evento recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 10133, Level = LogLevel.Error, Message = "Falha inesperada ao publicar um evento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10134, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da publicação de evento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
