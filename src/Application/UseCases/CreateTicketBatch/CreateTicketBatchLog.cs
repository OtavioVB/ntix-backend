using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateTicketBatch;

internal static partial class CreateTicketBatchLog
{
    [LoggerMessage(EventId = 10101, Level = LogLevel.Information,
        Message = "Lote {BatchId} criado no evento {EventId} da produtora {ProducerSlug}: {PriceCents} centavos, {Quantity} ingressos.")]
    public static partial void Created(ILogger logger, Guid batchId, Guid eventId, string producerSlug, long priceCents, int quantity);

    [LoggerMessage(EventId = 10102, Level = LogLevel.Warning,
        Message = "Criação de lote recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 10103, Level = LogLevel.Error, Message = "Falha inesperada ao criar um lote.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 10104, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da criação de lote.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
