using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.ValueObjects;

public readonly struct FullNameValueObject
{
    public bool IsValid { get; }

    private readonly string? _fullName;
    public string Value => IsValid
        ? _fullName!
        : throw new InvalidOperationException("It is not possible to get full name because value object is invalid");

    public MethodResult MethodResult { get; }

    private FullNameValueObject(bool isValid, MethodResult methodResult, string? fullName = null)
    {
        IsValid = isValid;
        _fullName = fullName;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 256;

    private const string FULL_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "FULL_NAME_COULD_NOT_BE_EMPTY";
    private const string FULL_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O nome completo não pode ser vazio.";

    private const string FULL_NAME_LENGTH_INVALID_NOTIFICATION_CODE = "FULL_NAME_LENGTH_INVALID";
    private const string FULL_NAME_LENGTH_INVALID_NOTIFICATION_MESSAGE = "O nome completo excede o tamanho máximo permitido.";

    public static FullNameValueObject Build(string? fullName)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 1;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        var normalized = Normalize(fullName);

        if (normalized.Length == 0)
        {
            notifications.Add(Notification.Error(
                code: FULL_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: FULL_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE));
        }

        if (normalized.Length > MAX_LENGTH)
        {
            notifications.Add(Notification.Error(
                code: FULL_NAME_LENGTH_INVALID_NOTIFICATION_CODE,
                message: FULL_NAME_LENGTH_INVALID_NOTIFICATION_MESSAGE));
        }

        if (notifications.HasAnyNotifications())
        {
            return new FullNameValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new FullNameValueObject(
            isValid: true,
            fullName: normalized,
            methodResult: MethodResult.Success());
    }

    private static string Normalize(string? fullName)
        => string.Join(' ', (fullName ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    public static implicit operator FullNameValueObject(string? obj)
        => Build(obj);
}
