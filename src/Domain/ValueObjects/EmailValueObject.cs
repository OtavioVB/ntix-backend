using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using System.Net.Mail;

namespace Domain.ValueObjects;

public readonly struct EmailValueObject
{
    public bool IsValid { get; }

    private readonly string? _email;
    public string Value => IsValid
        ? _email!
        : throw new InvalidOperationException("It is not possible to get email because value object is invalid");

    public MethodResult MethodResult { get; }

    private EmailValueObject(bool isValid, MethodResult methodResult, string? email = null)
    {
        IsValid = isValid;
        _email = email;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 256;

    private const int OBFUSCATION_VISIBLE_LENGTH = 2;
    private const string OBFUSCATION_MASK = "***";

    private const string EMAIL_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "EMAIL_COULD_NOT_BE_EMPTY";
    private const string EMAIL_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O email não pode ser vazio.";

    private const string EMAIL_LENGTH_INVALID_NOTIFICATION_CODE = "EMAIL_LENGTH_INVALID";
    private const string EMAIL_LENGTH_INVALID_NOTIFICATION_MESSAGE = "O email excede o tamanho máximo permitido.";

    private const string EMAIL_FORMAT_INVALID_NOTIFICATION_CODE = "EMAIL_FORMAT_INVALID";
    private const string EMAIL_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O email informado não possui um formato válido.";

    public static EmailValueObject Build(string? email)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 2;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length == 0)
        {
            notifications.Add(Notification.Error(
                code: EMAIL_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: EMAIL_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE));
        }
        else
        {
            if (normalized.Length > MAX_LENGTH)
            {
                notifications.Add(Notification.Error(
                    code: EMAIL_LENGTH_INVALID_NOTIFICATION_CODE,
                    message: EMAIL_LENGTH_INVALID_NOTIFICATION_MESSAGE));
            }

            if (!HasValidFormat(normalized))
            {
                notifications.Add(Notification.Error(
                    code: EMAIL_FORMAT_INVALID_NOTIFICATION_CODE,
                    message: EMAIL_FORMAT_INVALID_NOTIFICATION_MESSAGE));
            }
        }

        if (notifications.HasAnyNotifications())
        {
            return new EmailValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new EmailValueObject(
            isValid: true,
            email: normalized,
            methodResult: MethodResult.Success());
    }

    public string GetObfuscatedValue()
    {
        var email = Value;

        var domainSeparatorIndex = email.LastIndexOf('@');
        var visibleLength = domainSeparatorIndex > OBFUSCATION_VISIBLE_LENGTH ? OBFUSCATION_VISIBLE_LENGTH : 1;

        return $"{email[..visibleLength]}{OBFUSCATION_MASK}{email[domainSeparatorIndex..]}";
    }

    private static bool HasValidFormat(string email)
    {
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static implicit operator EmailValueObject(string? obj)
        => Build(obj);
}
