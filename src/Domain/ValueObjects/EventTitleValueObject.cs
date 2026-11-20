using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Título do evento (RN2): de 3 a 120 caracteres, com espaços repetidos reduzidos a um.</summary>
public readonly struct EventTitleValueObject
{
    public bool IsValid { get; }

    private readonly string? _title;
    public string Value => IsValid
        ? _title!
        : throw new InvalidOperationException("It is not possible to get event title because value object is invalid");

    public MethodResult MethodResult { get; }

    private EventTitleValueObject(bool isValid, MethodResult methodResult, string? title = null)
    {
        IsValid = isValid;
        _title = title;
        MethodResult = methodResult;
    }

    public const int MIN_LENGTH = 3;
    public const int MAX_LENGTH = 120;

    private const string EVENT_TITLE_INVALID_NOTIFICATION_CODE = "EVENT_TITLE_INVALID";
    private const string EVENT_TITLE_INVALID_NOTIFICATION_MESSAGE = "O título do evento deve ter de 3 a 120 caracteres.";

    public static EventTitleValueObject Build(string? title)
    {
        var normalized = string.Join(' ', (title ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length < MIN_LENGTH || normalized.Length > MAX_LENGTH)
        {
            var notification = Notification.Error(
                code: EVENT_TITLE_INVALID_NOTIFICATION_CODE,
                message: EVENT_TITLE_INVALID_NOTIFICATION_MESSAGE);

            return new EventTitleValueObject(isValid: false, methodResult: MethodResult.Error([notification]));
        }

        return new EventTitleValueObject(isValid: true, title: normalized, methodResult: MethodResult.Success());
    }

    public static implicit operator EventTitleValueObject(string? obj)
        => Build(obj);
}
