using Microsoft.Extensions.Logging;

namespace Application.UseCases.RefreshProducerSession;

internal static partial class RefreshProducerSessionLog
{
    [LoggerMessage(EventId = 5201, Level = LogLevel.Information,
        Message = "Sessão {SessionId} do usuário {ProducerUserId} renovada pela sessão {NextSessionId}, da família {FamilyId}.")]
    public static partial void Refreshed(ILogger logger, Guid sessionId, Guid producerUserId, Guid nextSessionId, Guid familyId);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Warning,
        Message = "Renovação recusada: refresh token {State}.")]
    public static partial void InvalidToken(ILogger logger, string state);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Warning,
        Message = "Reuso do refresh token já substituído da sessão {SessionId}, do usuário {ProducerUserId}: {RevokedCount} sessões da família {FamilyId} revogadas.")]
    public static partial void ReuseDetected(ILogger logger, Guid sessionId, Guid producerUserId, Guid familyId, int revokedCount);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Warning,
        Message = "Renovação recusada: o usuário {ProducerUserId} está bloqueado.")]
    public static partial void Blocked(ILogger logger, Guid producerUserId);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Warning,
        Message = "Renovação recusada por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 5206, Level = LogLevel.Error,
        Message = "Falha inesperada na renovação da sessão do usuário de produtora.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5207, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da renovação da sessão do usuário de produtora.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
