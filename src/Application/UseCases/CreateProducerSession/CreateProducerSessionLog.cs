using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateProducerSession;

internal static partial class CreateProducerSessionLog
{
    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "Login aceito: usuário {ProducerUserId} da produtora {ProducerSlug}, sessão {SessionId}.")]
    public static partial void Created(ILogger logger, Guid producerUserId, string producerSlug, Guid sessionId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning,
        Message = "Login recusado por {Reason} para o e-mail {ObfuscatedEmail}.")]
    public static partial void Rejected(ILogger logger, string reason, string obfuscatedEmail);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning,
        Message = "Login recusado pelo limite de tentativas para o e-mail {ObfuscatedEmail}.")]
    public static partial void RateLimited(ILogger logger, string obfuscatedEmail);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Warning,
        Message = "Login recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Error,
        Message = "Falha inesperada no login do usuário de produtora.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação do login do usuário de produtora.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
