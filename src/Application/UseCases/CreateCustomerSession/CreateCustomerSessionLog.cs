using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateCustomerSession;

/// <remarks>O login aparece só ofuscado (<c>***.456.789-**</c> ou <c>ma***@ntix.com</c>); a senha nunca.</remarks>
internal static partial class CreateCustomerSessionLog
{
    [LoggerMessage(EventId = 12101, Level = LogLevel.Information,
        Message = "Cliente {CustomerId} entrou na Ntix: sessão {SessionId} aberta.")]
    public static partial void Created(ILogger logger, Guid customerId, Guid sessionId);

    [LoggerMessage(EventId = 12102, Level = LogLevel.Warning,
        Message = "Login de cliente recusado por credencial inválida ({LoginKind} {ObfuscatedLogin}).")]
    public static partial void Rejected(ILogger logger, string loginKind, string obfuscatedLogin);

    [LoggerMessage(EventId = 12103, Level = LogLevel.Warning,
        Message = "Login de cliente recusado pelo limite de tentativas ({ObfuscatedLogin}).")]
    public static partial void RateLimited(ILogger logger, string obfuscatedLogin);

    [LoggerMessage(EventId = 12104, Level = LogLevel.Warning,
        Message = "Login de cliente recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 12105, Level = LogLevel.Error, Message = "Falha inesperada no login do cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 12106, Level = LogLevel.Error, Message = "Falha ao desfazer a transação do login do cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
