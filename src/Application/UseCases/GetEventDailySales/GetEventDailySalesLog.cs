using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetEventDailySales;

internal static partial class GetEventDailySalesLog
{
    [LoggerMessage(EventId = 14111, Level = LogLevel.Information,
        Message = "Vendas por dia do evento {EventId} consultadas pela produtora {ProducerSlug}: {Days} dia(s), {DaysWithSales} com venda.")]
    public static partial void Served(ILogger logger, Guid eventId, string producerSlug, int days, int daysWithSales);

    [LoggerMessage(EventId = 14112, Level = LogLevel.Warning,
        Message = "Consulta de vendas por dia recusada para a produtora {ProducerSlug}: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 14113, Level = LogLevel.Error, Message = "Falha inesperada ao consultar as vendas por dia.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 14114, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta de vendas por dia.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
