using Microsoft.Extensions.Logging;

namespace Application.UseCases.ConfirmPaymentFromWebhook;

/// <summary>Nenhuma mensagem leva o corpo do webhook, o segredo, a assinatura, a API_KEY nem o nome ou o CPF do comprador.</summary>
internal static partial class ConfirmPaymentFromWebhookLog
{
    [LoggerMessage(EventId = 7201, Level = LogLevel.Information,
        Message = "Webhook {EventId} confirmou o pagamento {PaymentId} do pedido {OrderCode}: {TicketCount} ingresso(s) emitido(s).")]
    public static partial void Confirmed(ILogger logger, string eventId, Guid paymentId, string orderCode, int ticketCount);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning,
        Message = "Webhook recusado: {Reason}.")]
    public static partial void Refused(ILogger logger, string reason);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning,
        Message = "Webhook {EventId} não confirma o pagamento {PaymentId}: a fonte informa {Status}.")]
    public static partial void NotPaidAtSource(ILogger logger, string eventId, Guid paymentId, string status);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Information,
        Message = "Webhook {EventId} ignorado: o tipo {EventType} não confirma pagamento.")]
    public static partial void Ignored(ILogger logger, string eventId, string eventType);

    [LoggerMessage(EventId = 7205, Level = LogLevel.Warning,
        Message = "Webhook {EventId} sem pagamento correspondente ao id informado pelo fornecedor.")]
    public static partial void PaymentNotFound(ILogger logger, string eventId);

    [LoggerMessage(EventId = 7206, Level = LogLevel.Error,
        Message = "Valor do pagamento {PaymentId} diverge do pedido {OrderCode}: pagamento não confirmado (RN9).")]
    public static partial void AmountMismatch(ILogger logger, Guid paymentId, string orderCode);

    [LoggerMessage(EventId = 7207, Level = LogLevel.Warning,
        Message = "Pagamento {PaymentId} confirmado depois do vencimento da reserva do pedido {OrderCode}: pedido honrado e sinalizado (RN10).")]
    public static partial void PaidAfterExpiration(ILogger logger, Guid paymentId, string orderCode);

    [LoggerMessage(EventId = 7208, Level = LogLevel.Information,
        Message = "Webhook {EventId} já processado para o pagamento {PaymentId}: nada muda.")]
    public static partial void Duplicate(ILogger logger, string eventId, Guid paymentId);

    [LoggerMessage(EventId = 7209, Level = LogLevel.Warning,
        Message = "Consulta na fonte indisponível ({Reason}) para o webhook {EventId}: o AbacatePay vai reenviar.")]
    public static partial void SourceUnavailable(ILogger logger, string eventId, string reason);

    [LoggerMessage(EventId = 7210, Level = LogLevel.Error,
        Message = "Falha inesperada ao confirmar o pagamento pelo webhook.")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7211, Level = LogLevel.Error,
        Message = "Falha ao desfazer a transação da confirmação do webhook.")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);
}
