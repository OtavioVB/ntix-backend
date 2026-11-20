using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetCurrentCustomer;

internal static partial class GetCurrentCustomerLog
{
    [LoggerMessage(EventId = 12131, Level = LogLevel.Information, Message = "Cliente {CustomerId} consultou os próprios dados.")]
    public static partial void Served(ILogger logger, Guid customerId);

    [LoggerMessage(EventId = 12132, Level = LogLevel.Warning, Message = "Consulta dos dados do cliente com token de conta inexistente.")]
    public static partial void NotFound(ILogger logger);

    [LoggerMessage(EventId = 12133, Level = LogLevel.Error, Message = "Falha inesperada ao consultar os dados do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12134, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta dos dados do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
