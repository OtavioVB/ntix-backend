using Microsoft.Extensions.Logging;

namespace Application.UseCases.CreateOrderPayment;

/// <summary>Nenhuma mensagem leva a API_KEY, o brCode, o CPF nem o e-mail do cliente.</summary>
internal static partial class CreateOrderPaymentLog
{
    [LoggerMessage(EventId = 7101, Level = LogLevel.Information,
        Message = "Pix {PaymentId} gerado para o pedido {OrderCode} pela integração {IntegrationId} ({Provider}), tentativa {Attempt}.")]
    public static partial void Created(ILogger logger, Guid paymentId, string orderCode, Guid integrationId, string provider, int attempt);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning,
        Message = "Tentativa {Attempt} falhou na integração {IntegrationId} ({Provider}) do pedido {OrderCode}: {Reason}.{Detail}")]
    public static partial void AttemptFailed(ILogger logger, int attempt, Guid integrationId, string provider, string orderCode, string reason, string detail);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Error, Message = "Nenhuma integração gerou o Pix do pedido {OrderCode}: {Attempts} tentativa(s).")]
    public static partial void AllFailed(ILogger logger, string orderCode, int attempts);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Information, Message = "Pix {PaymentId} do pedido {OrderCode} já existia e foi devolvido.")]
    public static partial void Reused(ILogger logger, Guid paymentId, string orderCode);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Warning, Message = "Pagamento recusado: {NotificationCodes}.")]
    public static partial void Rejected(ILogger logger, string notificationCodes);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Error, Message = "Falha inesperada ao gerar o Pix de um pedido.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Error, Message = "Falha ao desfazer a transação da geração do Pix.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
