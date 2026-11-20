using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateOrder;

internal static partial class CreateOrderLog
{
    [LoggerMessage(EventId = 6101, Level = LogLevel.Information,
        Message = "Pedido {OrderCode} criado no evento {EventId} do produtor {ProducerId}: {TicketCount} ingresso(s), {TotalCents} centavos.")]
    public static partial void Created(ILogger logger, string orderCode, Guid eventId, Guid producerId, int ticketCount, long totalCents);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning, Message = "Pedido recusado: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 6103, Level = LogLevel.Information, Message = "Cliente vinculado ao produtor {ProducerId} no primeiro pedido.")]
    public static partial void CustomerLinked(ILogger logger, Guid producerId);

    [LoggerMessage(EventId = 6104, Level = LogLevel.Error, Message = "Falha inesperada ao criar um pedido.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6105, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da criação de pedido.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
