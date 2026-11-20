using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Situação do evento usada como filtro, aceita só pelo nome exato.</summary>
public readonly struct EventStatusValueObject
{
    public bool IsValid { get; }

    private readonly EventStatus _value;
    public EventStatus Value => IsValid
        ? _value
        : throw new InvalidOperationException("It is not possible to get EventStatus because value object is invalid");

    public MethodResult MethodResult { get; }

    private EventStatusValueObject(bool isValid, MethodResult methodResult, EventStatus value = default)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    private const string EVENT_STATUS_INVALID_NOTIFICATION_CODE = "EVENT_STATUS_INVALID";
    private const string EVENT_STATUS_INVALID_NOTIFICATION_MESSAGE = "A situação do evento deve ser DRAFT ou PUBLISHED.";

    public static EventStatusValueObject Build(string? value)
    {
        if (value is null || !Enum.GetNames<EventStatus>().Contains(value))
        {
            var notification = Notification.Error(
                code: EVENT_STATUS_INVALID_NOTIFICATION_CODE,
                message: EVENT_STATUS_INVALID_NOTIFICATION_MESSAGE);

            return new EventStatusValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new EventStatusValueObject(
            isValid: true,
            value: Enum.Parse<EventStatus>(value),
            methodResult: MethodResult.Success());
    }

    public static implicit operator EventStatusValueObject(string? obj)
        => Build(obj);
}
