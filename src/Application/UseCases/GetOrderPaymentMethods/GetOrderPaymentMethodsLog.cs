using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetOrderPaymentMethods;

internal static partial class GetOrderPaymentMethodsLog
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Métodos de pagamento do pedido {OrderCode} consultados: {Methods}.")]
    public static partial void Served(ILogger logger, string orderCode, string methods);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Pedido não encontrado para a consulta dos métodos de pagamento.")]
    public static partial void NotFound(ILogger logger);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Error, Message = "Falha inesperada ao consultar os métodos de pagamento de um pedido.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta dos métodos de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
