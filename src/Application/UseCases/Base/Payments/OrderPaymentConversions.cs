using Application.UseCases.Base.Orders.Outputs;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Payments.Models;

namespace Application.UseCases.Base.Payments;

/// <summary>Conversões da cobrança do pedido entre domínio, modelo de persistência e saída (REQ7).</summary>
public static class OrderPaymentConversions
{
    public static PaymentModel ToModel(Payment payment)
        => new(
            Id: payment.Id.Value,
            OrderId: payment.OrderId.Value,
            ProducerId: payment.ProducerId.Value,
            IntegrationId: payment.IntegrationId.Value,
            Method: payment.Method.ToString(),
            Amount: payment.AmountCents / 100m,
            Status: payment.Status.ToString(),
            ProviderReference: payment.ProviderReference,
            PixCopyPaste: payment.PixCopyPaste,
            PixQrCodeImage: payment.PixQrCodeImage,
            ExpiresAt: payment.ExpiresAt.Value,
            PaidAt: payment.PaidAt,
            CreatedAt: payment.CreatedAt.Value,
            CreatedCorrelationId: payment.CreatedCorrelationId.Value,
            UpdatedAt: payment.UpdatedAt.Value,
            UpdatedCorrelationId: payment.UpdatedCorrelationId.Value);

    public static Payment ToPayment(PaymentModel model)
        => Payment.Restore(
            id: IdValueObject.Build(model.Id.ToString()),
            orderId: IdValueObject.Build(model.OrderId.ToString()),
            producerId: IdValueObject.Build(model.ProducerId.ToString()),
            integrationId: IdValueObject.Build(model.IntegrationId.ToString()),
            method: Enum.Parse<PaymentMethod>(model.Method),
            amountCents: (long)decimal.Round(model.Amount * 100m, 0, MidpointRounding.AwayFromZero),
            status: Enum.Parse<PaymentStatus>(model.Status),
            providerReference: model.ProviderReference,
            pixCopyPaste: model.PixCopyPaste,
            pixQrCodeImage: model.PixQrCodeImage,
            expiresAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.ExpiresAt, DateTimeKind.Utc)),
            paidAt: model.PaidAt is null ? null : DateTime.SpecifyKind(model.PaidAt.Value, DateTimeKind.Utc),
            createdAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc)),
            createdCorrelationId: IdValueObject.Build(model.CreatedCorrelationId.ToString()),
            updatedAt: DateTimeValueObject.Build(DateTime.SpecifyKind(model.UpdatedAt, DateTimeKind.Utc)),
            updatedCorrelationId: IdValueObject.Build(model.UpdatedCorrelationId.ToString()));

    /// <summary>O bloco <c>pix</c> só sai com a cobrança aguardando pagamento e dentro da validade.</summary>
    public static OrderPaymentOutput ToOutput(Payment payment, DateTime now)
    {
        var status = payment.GetEffectiveStatus(now);

        return new OrderPaymentOutput(
            Id: payment.Id.Value,
            Method: payment.Method.ToString(),
            Status: status.ToString(),
            AmountCents: payment.AmountCents,
            ExpiresAt: payment.ExpiresAt.Value,
            Pix: status == PaymentStatus.PENDING ? new PixOutput(payment.PixCopyPaste, payment.PixQrCodeImage) : null);
    }
}
