using Microsoft.Extensions.Logging;

namespace Application.UseCases.RefreshCustomerSession;

internal static partial class RefreshCustomerSessionLog
{
    [LoggerMessage(EventId = 12111, Level = LogLevel.Information,
        Message = "Sessão {SessionId} do cliente {CustomerId} renovada para a sessão {NextSessionId} (família {FamilyId}).")]
    public static partial void Refreshed(ILogger logger, Guid sessionId, Guid customerId, Guid nextSessionId, Guid familyId);

    [LoggerMessage(EventId = 12112, Level = LogLevel.Warning,
        Message = "Renovação de sessão de cliente recusada: refresh token {State}.")]
    public static partial void InvalidToken(ILogger logger, string state);

    [LoggerMessage(EventId = 12113, Level = LogLevel.Warning,
        Message = "Reuso de refresh token do cliente {CustomerId}: família {FamilyId} revogada ({RevokedCount} sessão(ões)).")]
    public static partial void ReuseDetected(ILogger logger, Guid customerId, Guid familyId, int revokedCount);

    [LoggerMessage(EventId = 12114, Level = LogLevel.Error, Message = "Falha inesperada na renovação da sessão do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12115, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da renovação da sessão do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
