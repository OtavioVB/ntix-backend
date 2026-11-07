using Microsoft.Extensions.Logging;

namespace Application.UseCases.GetProducerWhitelabel;

/// <remarks>O slug de produtora é nome comercial, não dado pessoal: aparece nos logs desta consulta (REQ3).</remarks>
internal static partial class GetProducerWhitelabelLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Configuração whitelabel da produtora {Slug} servida.")]
    public static partial void Served(ILogger logger, string slug);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Consulta de whitelabel recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Nenhum arquivo de whitelabel para a produtora {Slug}.")]
    public static partial void NotFound(ILogger logger, string slug);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error,
        Message = "Arquivo de whitelabel da produtora {Slug} fora da estrutura ({ReadStatus}): {InvalidFields}. A configuração não foi servida.")]
    public static partial void InvalidFile(ILogger logger, string slug, string readStatus, string invalidFields);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error,
        Message = "Falha inesperada ao consultar o whitelabel da produtora {Slug}.")]
    public static partial void Failed(ILogger logger, string slug, Exception exception);
}
