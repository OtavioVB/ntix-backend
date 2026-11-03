using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateProducer;

internal static partial class CreateProducerLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Produtora {ProducerId} cadastrada em PENDING com o email {ObfuscatedEmail} e o administrador {ProducerUserId} em PENDING, aguardando a verificação do email.")]
    public static partial void Created(ILogger logger, Guid producerId, Guid producerUserId, string obfuscatedEmail);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Cadastro de produtora recusado: o email {ObfuscatedEmail} já pertence a uma produtora ou a um usuário de produtora.")]
    public static partial void AlreadyExists(ILogger logger, string obfuscatedEmail);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Cadastro de produtora recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Slug ocupado por um cadastro simultâneo do email {ObfuscatedEmail}: gerando outra versão.")]
    public static partial void SlugTaken(ILogger logger, string obfuscatedEmail);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error,
        Message = "Falha inesperada ao cadastrar a produtora.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação do cadastro de produtora.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
