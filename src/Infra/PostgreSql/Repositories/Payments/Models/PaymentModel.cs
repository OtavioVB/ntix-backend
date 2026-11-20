namespace Infra.PostgreSql.Repositories.Payments.Models;

/// <summary>Linha de <c>payments.payment</c>, com tipos primitivos. Valor em reais (<c>NUMERIC(18,2)</c>).</summary>
public sealed record PaymentModel(
    Guid Id,
    Guid OrderId,
    Guid ProducerId,
    Guid IntegrationId,
    string Method,
    decimal Amount,
    string Status,
    string ProviderReference,
    string PixCopyPaste,
    string PixQrCodeImage,
    DateTime ExpiresAt,
    DateTime? PaidAt,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime UpdatedAt,
    Guid UpdatedCorrelationId);
