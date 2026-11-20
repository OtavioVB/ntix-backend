using Domain.BoundedContexts.OrderContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Canal da venda do pedido (REQ6, RN2), aceito só pelo nome exato: HUB ou STORE.</summary>
public readonly struct SalesChannelValueObject
{
    public bool IsValid { get; }

    private readonly SalesChannel _value;
    public SalesChannel Value => IsValid
        ? _value
        : throw new InvalidOperationException("It is not possible to get SalesChannel because value object is invalid");

    public MethodResult MethodResult { get; }

    private SalesChannelValueObject(bool isValid, MethodResult methodResult, SalesChannel value = default)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const string ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_CODE = "ORDER_SALES_CHANNEL_INVALID";
    private const string ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_MESSAGE = "O canal de venda deve ser HUB ou STORE.";

    public static SalesChannelValueObject Build(string? value)
    {
        if (value is null || !Enum.GetNames<SalesChannel>().Contains(value))
        {
            var notification = Notification.Error(ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_CODE, ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_MESSAGE);

            return new SalesChannelValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new SalesChannelValueObject(isValid: true, value: Enum.Parse<SalesChannel>(value), methodResult: MethodResult.Success());
    }

    public static implicit operator SalesChannelValueObject(string? obj)
        => Build(obj);
}
