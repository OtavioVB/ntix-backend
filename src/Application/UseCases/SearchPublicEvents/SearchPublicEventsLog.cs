using Microsoft.Extensions.Logging;

namespace Application.UseCases.SearchPublicEvents;

internal static partial class SearchPublicEventsLog
{
    [LoggerMessage(EventId = 11101, Level = LogLevel.Information,
        Message = "Vitrine consultada (produtora {ProducerSlug}, categoria {Category}, UF {State}), página {Page}: {Count} de {TotalItems} evento(s).")]
    public static partial void Served(ILogger logger, string producerSlug, string category, string state, int page, int count, int totalItems);

    [LoggerMessage(EventId = 11102, Level = LogLevel.Warning,
        Message = "Consulta da vitrine recusada por filtro inválido: {Reasons}")]
    public static partial void InvalidFilter(ILogger logger, string reasons);

    [LoggerMessage(EventId = 11103, Level = LogLevel.Error, Message = "Falha inesperada ao consultar a vitrine.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 11104, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da consulta da vitrine.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
