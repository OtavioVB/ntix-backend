using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetProducerEvent;

internal static partial class GetProducerEventLog
{
    [LoggerMessage(EventId = 9121, Level = LogLevel.Information,
        Message = "Evento {EventId} consultado pela produtora {ProducerSlug}.")]
    public static partial void Served(ILogger logger, Guid eventId, string producerSlug);

    [LoggerMessage(EventId = 9122, Level = LogLevel.Warning,
        Message = "Consulta de evento recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 9123, Level = LogLevel.Warning,
        Message = "Consulta de evento recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9124, Level = LogLevel.Warning,
        Message = "Consulta de evento recusada: o evento não existe na produtora {ProducerSlug}.")]
    public static partial void EventNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 9125, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar um evento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9126, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da consulta de evento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
