using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Quantidade de ingressos de um item do pedido (REQ6, RN3): inteira, de 1 a 100.000 (o teto do lote).</summary>
public readonly struct OrderItemQuantityValueObject
{
    public bool IsValid { get; }

    private readonly int _quantity;
    public int Value => IsValid
        ? _quantity
        : throw new InvalidOperationException("It is not possible to get order item quantity because value object is invalid");

    public MethodResult MethodResult { get; }

    private OrderItemQuantityValueObject(bool isValid, MethodResult methodResult, int quantity = 0)
    {
        IsValid = isValid;
        _quantity = quantity;
        MethodResult = methodResult;
    }

    public const string ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_CODE = "ORDER_ITEM_QUANTITY_INVALID";
    private const string ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_MESSAGE = "A quantidade de cada item do pedido deve ser um número inteiro de 1 em diante.";

    public static OrderItemQuantityValueObject Build(int? quantity)
    {
        if (quantity is null or < 1 or > BatchQuantityValueObject.MAX)
        {
            var notification = Notification.Error(ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_CODE, ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_MESSAGE);

            return new OrderItemQuantityValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new OrderItemQuantityValueObject(isValid: true, quantity: quantity.Value, methodResult: MethodResult.Success());
    }
}
