using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetCustomerTicket;

/// <summary>Ingresso inexistente é registrado com o código ofuscado nos 4 últimos caracteres (REQ13).</summary>
internal static partial class GetCustomerTicketLog
{
    [LoggerMessage(EventId = 7321, Level = LogLevel.Warning,
        Message = "Ingresso ...{CodeSuffix} não encontrado para o cliente da sessão.")]
    public static partial void NotFoundSuffix(ILogger logger, string codeSuffix);

    [LoggerMessage(EventId = 7322, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar o ingresso do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7323, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da consulta do ingresso do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);

    public static void NotFound(ILogger logger, string code)
        => NotFoundSuffix(logger, code.Length >= 4 ? code[^4..] : code);
}
