using Domain.CrossCutting.NotificationContext.Enums;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.CrossCutting.NotificationContext;

public readonly struct Notification : INotification
{
    private Notification(string code, string message, TypeNotification type)
    {
        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; }
    public string Message { get; }
    public TypeNotification Type { get; }

    private const int _codeMaxLength = 124;
    private const int _messageMaxLength = 256;

    public static Notification Build(string code, string message, TypeNotification type)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > _codeMaxLength)
            throw new ArgumentException("The notification code must be valid.", nameof(code));

        if (string.IsNullOrWhiteSpace(message) || message.Length > _messageMaxLength)
            throw new ArgumentException("The notification message must be valid.", nameof(message));

        if (!Enum.IsDefined(type))
            throw new ArgumentException("The notification type must be valid.", nameof(type));

        return new(code, message, type);
    }

    public static Notification Success(string code, string message)
        => Build(code, message, TypeNotification.Success);

    public static Notification Information(string code, string message)
        => Build(code, message, TypeNotification.Information);

    public static Notification Error(string code, string message)
        => Build(code, message, TypeNotification.Error);
}

public static class NotificationExtensions
{
    public static bool HasAnyNotifications(this IEnumerable<INotification> notifications)
        => notifications.Any();
}
