namespace Infra.PostgreSql.Repositories.Payments.Models;

/// <summary>Linha de <c>payments.payment_webhook_event</c> (REQ8). <c>PaymentId</c> é nulo quando o pagamento não foi encontrado.</summary>
public sealed record PaymentWebhookEventModel(
    Guid Id,
    string ProviderCode,
    string ProviderEventId,
    string EventType,
    Guid? PaymentId,
    string Outcome,
    DateTime ReceivedAt,
    Guid CreatedCorrelationId);
