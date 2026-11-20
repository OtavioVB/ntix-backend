using Microsoft.Extensions.Logging;

namespace Application.UseCases.UpdateTicketBatch;

internal static partial class UpdateTicketBatchLog
{
    [LoggerMessage(EventId = 10111, Level = LogLevel.Information,
        Message = "Lote {BatchId} do evento {EventId} da produtora {ProducerSlug} alterado: {Status}, {PriceCents} centavos, {Quantity} ingressos.")]
    public static partial void Updated(ILogger logger, Guid batchId, Guid eventId, string producerSlug, string status, long priceCents, int quantity);

    [LoggerMessage(EventId = 10112, Level = LogLevel.Warning,
        Message = "Alteração de lote recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 10113, Level = LogLevel.Error, Message = "Falha inesperada ao alterar um lote.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10114, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da alteração de lote.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
