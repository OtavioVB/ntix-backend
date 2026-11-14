using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Método de pagamento informado por quem chama (rota), aceito só pelo nome exato: PIX, BOLETO ou CREDIT_CARD (RN2).</summary>
public readonly struct PaymentMethodValueObject
{
    public bool IsValid { get; }

    private readonly PaymentMethod _paymentMethod;
    public PaymentMethod Value => IsValid
        ? _paymentMethod
        : throw new InvalidOperationException("It is not possible to get payment method because value object is invalid");

    public MethodResult MethodResult { get; }

    private PaymentMethodValueObject(bool isValid, MethodResult methodResult, PaymentMethod paymentMethod = default)
    {
        IsValid = isValid;
        _paymentMethod = paymentMethod;
        MethodResult = methodResult;
    }

    private const string PAYMENT_METHOD_INVALID_NOTIFICATION_CODE = "PAYMENT_METHOD_INVALID";
    private const string PAYMENT_METHOD_INVALID_NOTIFICATION_MESSAGE = "O método de pagamento deve ser PIX, BOLETO ou CREDIT_CARD.";

    public static PaymentMethodValueObject Build(string? paymentMethod)
    {
        // Enum.TryParse aceitaria "1" e "pix": só o nome exato do enum é método válido.
        if (paymentMethod is null || !Enum.GetNames<PaymentMethod>().Contains(paymentMethod))
        {
            var notification = Notification.Error(
                code: PAYMENT_METHOD_INVALID_NOTIFICATION_CODE,
                message: PAYMENT_METHOD_INVALID_NOTIFICATION_MESSAGE);

            return new PaymentMethodValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new PaymentMethodValueObject(
            isValid: true,
            paymentMethod: Enum.Parse<PaymentMethod>(paymentMethod),
            methodResult: MethodResult.Success());
    }

    public static implicit operator PaymentMethodValueObject(string? obj)
        => Build(obj);
}
