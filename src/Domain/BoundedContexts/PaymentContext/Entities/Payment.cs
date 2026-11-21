using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.PaymentContext.Entities;

/// <summary>
/// Cobrança Pix de um pedido (REQ7), gravada com a integração que a gerou: a confirmação (REQ8) volta sempre a ela, mesmo que
/// a rota do produtor mude depois (RN9).
/// </summary>
public sealed record Payment
{
    private Payment(
        IdValueObject id, IdValueObject orderId, IdValueObject producerId, IdValueObject integrationId, PaymentMethod method, long amountCents,
        PaymentStatus status, string providerReference, string pixCopyPaste, string pixQrCodeImage, DateTimeValueObject expiresAt, DateTime? paidAt,
        DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
    {
        Id = id;
        OrderId = orderId;
        ProducerId = producerId;
        IntegrationId = integrationId;
        Method = method;
        AmountCents = amountCents;
        Status = status;
        ProviderReference = providerReference;
        PixCopyPaste = pixCopyPaste;
        PixQrCodeImage = pixQrCodeImage;
        ExpiresAt = expiresAt;
        PaidAt = paidAt;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        UpdatedAt = updatedAt;
        UpdatedCorrelationId = updatedCorrelationId;
    }

    public IdValueObject Id { get; init; }
    public IdValueObject OrderId { get; init; }
    public IdValueObject ProducerId { get; init; }
    public IdValueObject IntegrationId { get; init; }
    public PaymentMethod Method { get; init; }
    public long AmountCents { get; init; }
    public PaymentStatus Status { get; init; }
    public string ProviderReference { get; init; }
    public string PixCopyPaste { get; init; }
    public string PixQrCodeImage { get; init; }
    public DateTimeValueObject ExpiresAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject UpdatedAt { get; init; }
    public IdValueObject UpdatedCorrelationId { get; init; }

    /// <summary>Pix recém-gerado no fornecedor. O id é escolhido antes da chamada, porque vai como <c>externalId</c> (RN8).</summary>
    public static Payment CreatePix(
        IdValueObject id, IdValueObject orderId, IdValueObject producerId, IdValueObject integrationId, long amountCents,
        string providerReference, string pixCopyPaste, string pixQrCodeImage, DateTime expiresAt, DateTime now, IdValueObject correlationId)
        => new(
            id, orderId, producerId, integrationId, PaymentMethod.PIX, amountCents, PaymentStatus.PENDING, providerReference, pixCopyPaste,
            pixQrCodeImage, DateTimeValueObject.Build(expiresAt), null, DateTimeValueObject.Build(now), correlationId, DateTimeValueObject.Build(now), correlationId);

    /// <summary>Reconstrói a cobrança gravada.</summary>
    public static Payment Restore(
        IdValueObject id, IdValueObject orderId, IdValueObject producerId, IdValueObject integrationId, PaymentMethod method, long amountCents,
        PaymentStatus status, string providerReference, string pixCopyPaste, string pixQrCodeImage, DateTimeValueObject expiresAt, DateTime? paidAt,
        DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
        => new(id, orderId, producerId, integrationId, method, amountCents, status, providerReference, pixCopyPaste, pixQrCodeImage, expiresAt, paidAt, createdAt, createdCorrelationId, updatedAt, updatedCorrelationId);

    /// <summary>Cobrança aguardando pagamento cujo Pix venceu aparece como <see cref="PaymentStatus.EXPIRED"/>.</summary>
    public PaymentStatus GetEffectiveStatus(DateTime now)
        => Status == PaymentStatus.PENDING && ExpiresAt.Value <= now ? PaymentStatus.EXPIRED : Status;

    /// <summary>Só a cobrança aguardando pagamento e dentro da validade pode ser devolvida de novo (RN4).</summary>
    public bool IsPendingAndValid(DateTime now)
        => GetEffectiveStatus(now) == PaymentStatus.PENDING;

    /// <summary>RN9: o valor da cobrança é o que o pedido pediu na criação. Divergência não confirma.</summary>
    public bool HasAmount(long amountCents) => AmountCents == amountCents;

    /// <summary>REQ8: a cobrança confirmada na fonte vira PAID, com a data do pagamento.</summary>
    public Payment Confirm(DateTime paidAt, DateTime now, IdValueObject correlationId)
        => this with { Status = PaymentStatus.PAID, PaidAt = paidAt, UpdatedAt = DateTimeValueObject.Build(now), UpdatedCorrelationId = correlationId };

    public Payment MarkExpired(DateTime now, IdValueObject correlationId)
        => this with { Status = PaymentStatus.EXPIRED, UpdatedAt = DateTimeValueObject.Build(now), UpdatedCorrelationId = correlationId };
}
