using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetCustomerOrder;

internal static partial class GetCustomerOrderLog
{
    [LoggerMessage(EventId = 6111, Level = LogLevel.Information, Message = "Pedido {OrderCode} consultado: {Status}.")]
    public static partial void Served(ILogger logger, string orderCode, string status);

    [LoggerMessage(EventId = 6112, Level = LogLevel.Warning, Message = "Pedido não encontrado para o comprador.")]
    public static partial void NotFound(ILogger logger);

    [LoggerMessage(EventId = 6113, Level = LogLevel.Error, Message = "Falha inesperada ao consultar um pedido.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6114, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta de pedido.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
