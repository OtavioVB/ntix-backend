using Microsoft.Extensions.Logging;

namespace Application.UseCases.RevokeProducerSession;

internal static partial class RevokeProducerSessionLog
{
    [LoggerMessage(EventId = 5301, Level = LogLevel.Information,
        Message = "Logout do usuário {ProducerUserId}: {RevokedCount} sessões da família {FamilyId} revogadas.")]
    public static partial void Revoked(ILogger logger, Guid producerUserId, Guid familyId, int revokedCount);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information,
        Message = "Logout sem efeito: o refresh token não corresponde a nenhuma sessão.")]
    public static partial void NotFound(ILogger logger);

    [LoggerMessage(EventId = 5303, Level = LogLevel.Warning,
        Message = "Logout recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 5304, Level = LogLevel.Error,
        Message = "Falha inesperada no logout do usuário de produtora.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5305, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação do logout do usuário de produtora.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
