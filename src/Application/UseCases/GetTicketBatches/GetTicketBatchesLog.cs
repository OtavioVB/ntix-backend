using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetTicketBatches;

internal static partial class GetTicketBatchesLog
{
    [LoggerMessage(EventId = 10121, Level = LogLevel.Information,
        Message = "Lotes do evento {EventId} consultados pela produtora {ProducerSlug}: {Count} lote(s).")]
    public static partial void Served(ILogger logger, Guid eventId, string producerSlug, int count);

    [LoggerMessage(EventId = 10122, Level = LogLevel.Warning,
        Message = "Consulta de lotes recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 10123, Level = LogLevel.Error, Message = "Falha inesperada ao consultar os lotes.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10124, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta de lotes.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
