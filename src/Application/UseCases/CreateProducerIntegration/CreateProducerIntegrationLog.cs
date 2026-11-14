using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateProducerIntegration;

/// <remarks>Nunca registra valor de credencial: só as chaves.</remarks>
internal static partial class CreateProducerIntegrationLog
{
    [LoggerMessage(EventId = 4201, Level = LogLevel.Information,
        Message = "Integração {IntegrationId} criada em ACTIVE para a produtora {ProducerSlug} com o fornecedor {ProviderCode} e as credenciais {CredentialKeys}.")]
    public static partial void Created(ILogger logger, Guid integrationId, string producerSlug, string providerCode, string credentialKeys);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning,
        Message = "Conexão de fornecedor recusada para a produtora {ProducerSlug} por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning,
        Message = "Conexão de fornecedor recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Warning,
        Message = "Conexão de fornecedor recusada: a produtora {ProducerSlug} já tem uma integração com o nome informado.")]
    public static partial void NameAlreadyExists(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Error,
        Message = "Falha inesperada ao conectar um fornecedor de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da conexão de fornecedor de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
