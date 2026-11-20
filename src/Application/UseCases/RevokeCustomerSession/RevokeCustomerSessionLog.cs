using Microsoft.Extensions.Logging;

namespace Application.UseCases.RevokeCustomerSession;

internal static partial class RevokeCustomerSessionLog
{
    [LoggerMessage(EventId = 12121, Level = LogLevel.Information,
        Message = "Cliente {CustomerId} saiu da Ntix: família {FamilyId} revogada ({RevokedCount} sessão(ões)).")]
    public static partial void Revoked(ILogger logger, Guid customerId, Guid familyId, int revokedCount);

    [LoggerMessage(EventId = 12122, Level = LogLevel.Information,
        Message = "Logout de cliente com refresh token desconhecido: nada a revogar.")]
    public static partial void NotFound(ILogger logger);

    [LoggerMessage(EventId = 12123, Level = LogLevel.Error, Message = "Falha inesperada no logout do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12124, Level = LogLevel.Error, Message = "Falha ao desfazer a transação do logout do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
