using Microsoft.Extensions.Logging;

namespace Application.UseCases.UpdateEvent;

/// <remarks>Registra quais campos mudaram, nunca os valores de texto livre.</remarks>
internal static partial class UpdateEventLog
{
    [LoggerMessage(EventId = 9111, Level = LogLevel.Information,
        Message = "Evento {EventId} da produtora {ProducerSlug} alterado. Campos: {ChangedFields}.")]
    public static partial void Updated(ILogger logger, Guid eventId, string producerSlug, string changedFields);

    [LoggerMessage(EventId = 9112, Level = LogLevel.Warning,
        Message = "Alteração de evento recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 9113, Level = LogLevel.Warning,
        Message = "Alteração de evento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9114, Level = LogLevel.Warning,
        Message = "Alteração de evento recusada: o evento não existe na produtora {ProducerSlug}.")]
    public static partial void EventNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9115, Level = LogLevel.Error,
        Message = "Falha inesperada ao alterar um evento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9116, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da alteração de evento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
