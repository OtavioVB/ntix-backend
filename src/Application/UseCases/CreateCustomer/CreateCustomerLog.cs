using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateCustomer;

internal static partial class CreateCustomerLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Cliente {CustomerId} cadastrado com o email {ObfuscatedEmail} e o CPF {ObfuscatedCpf}.")]
    public static partial void Created(ILogger logger, Guid customerId, string obfuscatedEmail, string obfuscatedCpf);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Cadastro de cliente recusado: já existe um cliente com o email {ObfuscatedEmail} ou o CPF {ObfuscatedCpf}.")]
    public static partial void AlreadyExists(ILogger logger, string obfuscatedEmail, string obfuscatedCpf);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Cadastro de cliente recusado por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "Slug ocupado por um cadastro simultâneo do email {ObfuscatedEmail}: gerando outra versão.")]
    public static partial void SlugTaken(ILogger logger, string obfuscatedEmail);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error,
        Message = "Falha inesperada ao cadastrar o cliente.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação do cadastro de cliente.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
