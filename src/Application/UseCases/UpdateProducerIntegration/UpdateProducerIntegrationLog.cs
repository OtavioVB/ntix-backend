using Microsoft.Extensions.Logging;

namespace Application.UseCases.UpdateProducerIntegration;

/// <remarks>Nunca registra valor de credencial: só as chaves.</remarks>
internal static partial class UpdateProducerIntegrationLog
{
    [LoggerMessage(EventId = 4401, Level = LogLevel.Information,
        Message = "Integração {IntegrationId} da produtora {ProducerSlug} ({ProviderCode}) alterada: situação {PreviousStatus} para {Status}, credenciais gravadas [{ChangedCredentialKeys}], removidas [{RemovedCredentialKeys}].")]
    public static partial void Updated(ILogger logger, Guid integrationId, string producerSlug, string providerCode, string previousStatus, string status, string changedCredentialKeys, string removedCredentialKeys);

    [LoggerMessage(EventId = 4402, Level = LogLevel.Information,
        Message = "Integração {IntegrationId} da produtora {ProducerSlug} inativada: sai do ranqueamento e continua atendendo os pagamentos que já processou.")]
    public static partial void Inactivated(ILogger logger, Guid integrationId, string producerSlug);

    [LoggerMessage(EventId = 4403, Level = LogLevel.Warning,
        Message = "Alteração de integração recusada para a produtora {ProducerSlug} por dados inválidos: {NotificationCodes}.")]
    public static partial void InvalidData(ILogger logger, string producerSlug, string notificationCodes);

    [LoggerMessage(EventId = 4404, Level = LogLevel.Warning,
        Message = "Alteração de integração recusada: a produtora {ProducerSlug} não existe.")]
    public static partial void ProducerNotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4405, Level = LogLevel.Warning,
        Message = "Alteração de integração recusada: a integração não existe para a produtora {ProducerSlug}.")]
    public static partial void NotFound(ILogger logger, string producerSlug);

    [LoggerMessage(EventId = 4406, Level = LogLevel.Warning,
        Message = "Alteração da integração {IntegrationId} recusada: a produtora {ProducerSlug} já tem outra integração com o nome informado.")]
    public static partial void NameAlreadyExists(ILogger logger, Guid integrationId, string producerSlug);

    [LoggerMessage(EventId = 4407, Level = LogLevel.Error,
        Message = "Falha inesperada ao alterar uma integração de pagamento.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4408, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da alteração de integração de pagamento.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
