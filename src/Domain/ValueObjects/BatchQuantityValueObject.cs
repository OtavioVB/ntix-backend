using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Quantidade de ingressos do lote (REQ10, RN4): de 1 a 100.000.</summary>
public readonly struct BatchQuantityValueObject
{
    public bool IsValid { get; }

    private readonly int _quantity;
    public int Value => IsValid
        ? _quantity
        : throw new InvalidOperationException("It is not possible to get batch quantity because value object is invalid");

    public MethodResult MethodResult { get; }

    private BatchQuantityValueObject(bool isValid, MethodResult methodResult, int quantity = 0)
    {
        IsValid = isValid;
        _quantity = quantity;
        MethodResult = methodResult;
    }

    public const int MAX = 100_000;

    private const string BATCH_QUANTITY_INVALID_NOTIFICATION_CODE = "BATCH_QUANTITY_INVALID";
    private const string BATCH_QUANTITY_INVALID_NOTIFICATION_MESSAGE = "A quantidade do lote deve ser de 1 a 100.000 ingressos.";

    public static BatchQuantityValueObject Build(int? quantity)
    {
        if (quantity is null or < 1 or > MAX)
        {
            var notification = Notification.Error(BATCH_QUANTITY_INVALID_NOTIFICATION_CODE, BATCH_QUANTITY_INVALID_NOTIFICATION_MESSAGE);

            return new BatchQuantityValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new BatchQuantityValueObject(isValid: true, quantity: quantity.Value, methodResult: MethodResult.Success());
    }
}
