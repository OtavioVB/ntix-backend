using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Categoria do evento no hub (RN3), aceita só pelo nome exato.</summary>
public readonly struct EventCategoryValueObject
{
    public bool IsValid { get; }

    private readonly EventCategory _value;
    public EventCategory Value => IsValid
        ? _value
        : throw new InvalidOperationException("It is not possible to get EventCategory because value object is invalid");

    public MethodResult MethodResult { get; }

    private EventCategoryValueObject(bool isValid, MethodResult methodResult, EventCategory value = default)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    private const string EVENT_CATEGORY_INVALID_NOTIFICATION_CODE = "EVENT_CATEGORY_INVALID";
    private const string EVENT_CATEGORY_INVALID_NOTIFICATION_MESSAGE = "A categoria do evento deve ser uma de SHOWS, FESTIVALS, THEATER, STAND_UP, ELECTRONIC, SPORTS, GASTRONOMY, KIDS ou OTHER.";

    public static EventCategoryValueObject Build(string? value)
    {
        if (value is null || !Enum.GetNames<EventCategory>().Contains(value))
        {
            var notification = Notification.Error(
                code: EVENT_CATEGORY_INVALID_NOTIFICATION_CODE,
                message: EVENT_CATEGORY_INVALID_NOTIFICATION_MESSAGE);

            return new EventCategoryValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new EventCategoryValueObject(
            isValid: true,
            value: Enum.Parse<EventCategory>(value),
            methodResult: MethodResult.Success());
    }

    public static implicit operator EventCategoryValueObject(string? obj)
        => Build(obj);
}
