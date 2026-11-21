using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetCustomerTickets;

/// <summary>Sem dado pessoal nem código de ingresso completo: a lista traz quantidades e o período (REQ13).</summary>
internal static partial class GetCustomerTicketsLog
{
    [LoggerMessage(EventId = 7311, Level = LogLevel.Information,
        Message = "Ingressos do cliente listados (período {Period}), página {Page}: {Count} de {TotalItems}.")]
    public static partial void Served(ILogger logger, string period, int page, int count, int totalItems);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Error,
        Message = "Falha inesperada ao listar os ingressos do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7313, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da lista de ingressos do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
