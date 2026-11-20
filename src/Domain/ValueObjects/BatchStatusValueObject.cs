using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Situação do lote (REQ10, RN8), aceita só pelo nome exato: ACTIVE ou INACTIVE.</summary>
public readonly struct BatchStatusValueObject
{
    public bool IsValid { get; }

    private readonly BatchStatus _value;
    public BatchStatus Value => IsValid
        ? _value
        : throw new InvalidOperationException("It is not possible to get BatchStatus because value object is invalid");

    public MethodResult MethodResult { get; }

    private BatchStatusValueObject(bool isValid, MethodResult methodResult, BatchStatus value = default)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    private const string BATCH_STATUS_INVALID_NOTIFICATION_CODE = "BATCH_STATUS_INVALID";
    private const string BATCH_STATUS_INVALID_NOTIFICATION_MESSAGE = "A situação do lote deve ser ACTIVE ou INACTIVE.";

    public static BatchStatusValueObject Build(string? value)
    {
        if (value is null || !Enum.GetNames<BatchStatus>().Contains(value))
        {
            var notification = Notification.Error(
                code: BATCH_STATUS_INVALID_NOTIFICATION_CODE,
                message: BATCH_STATUS_INVALID_NOTIFICATION_MESSAGE);

            return new BatchStatusValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new BatchStatusValueObject(isValid: true, value: Enum.Parse<BatchStatus>(value), methodResult: MethodResult.Success());
    }

    public static implicit operator BatchStatusValueObject(string? obj)
        => Build(obj);
}
