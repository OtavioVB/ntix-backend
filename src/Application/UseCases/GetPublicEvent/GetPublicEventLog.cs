using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetPublicEvent;

internal static partial class GetPublicEventLog
{
    [LoggerMessage(EventId = 11111, Level = LogLevel.Information,
        Message = "Página do evento {EventSlug} consultada: {SaleState}, {BatchCount} lote(s) visível(is).")]
    public static partial void Served(ILogger logger, string eventSlug, string saleState, int batchCount);

    [LoggerMessage(EventId = 11112, Level = LogLevel.Warning,
        Message = "Página de evento não encontrada para o slug {EventSlug}.")]
    public static partial void NotFound(ILogger logger, string eventSlug);

    [LoggerMessage(EventId = 11113, Level = LogLevel.Error, Message = "Falha inesperada ao consultar a página do evento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11114, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da página do evento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
