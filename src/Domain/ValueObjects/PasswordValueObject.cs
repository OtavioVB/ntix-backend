using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.ValueObjects;

public readonly struct PasswordValueObject
{
    public bool IsValid { get; }

    private readonly string? _password;
    public string Value => IsValid
        ? _password!
        : throw new InvalidOperationException("It is not possible to get password because value object is invalid");

    public MethodResult MethodResult { get; }

    private PasswordValueObject(bool isValid, MethodResult methodResult, string? password = null)
    {
        IsValid = isValid;
        _password = password;
        MethodResult = methodResult;
    }

    public const int MIN_LENGTH = 8;

    private const string PASSWORD_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "PASSWORD_COULD_NOT_BE_EMPTY";
    private const string PASSWORD_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A senha não pode ser vazia.";

    private const string PASSWORD_TOO_SHORT_NOTIFICATION_CODE = "PASSWORD_TOO_SHORT";
    private const string PASSWORD_TOO_SHORT_NOTIFICATION_MESSAGE = "A senha deve ter ao menos 8 caracteres.";

    private const string PASSWORD_WITHOUT_UPPERCASE_NOTIFICATION_CODE = "PASSWORD_WITHOUT_UPPERCASE";
    private const string PASSWORD_WITHOUT_UPPERCASE_NOTIFICATION_MESSAGE = "A senha deve conter uma letra maiúscula.";

    private const string PASSWORD_WITHOUT_LOWERCASE_NOTIFICATION_CODE = "PASSWORD_WITHOUT_LOWERCASE";
    private const string PASSWORD_WITHOUT_LOWERCASE_NOTIFICATION_MESSAGE = "A senha deve conter uma letra minúscula.";

    private const string PASSWORD_WITHOUT_NUMBER_NOTIFICATION_CODE = "PASSWORD_WITHOUT_NUMBER";
    private const string PASSWORD_WITHOUT_NUMBER_NOTIFICATION_MESSAGE = "A senha deve conter um número.";

    private const string PASSWORD_WITHOUT_SYMBOL_NOTIFICATION_CODE = "PASSWORD_WITHOUT_SYMBOL";
    private const string PASSWORD_WITHOUT_SYMBOL_NOTIFICATION_MESSAGE = "A senha deve conter um símbolo.";

    public static PasswordValueObject Build(string? password)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 5;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        // Sem Trim: espaços fazem parte da senha que o cliente escolheu.
        var value = password ?? string.Empty;

        if (value.Length == 0)
        {
            notifications.Add(Notification.Error(
                code: PASSWORD_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: PASSWORD_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE));
        }
        else
        {
            if (value.Length < MIN_LENGTH)
            {
                notifications.Add(Notification.Error(
                    code: PASSWORD_TOO_SHORT_NOTIFICATION_CODE,
                    message: PASSWORD_TOO_SHORT_NOTIFICATION_MESSAGE));
            }

            if (!value.Any(char.IsUpper))
            {
                notifications.Add(Notification.Error(
                    code: PASSWORD_WITHOUT_UPPERCASE_NOTIFICATION_CODE,
                    message: PASSWORD_WITHOUT_UPPERCASE_NOTIFICATION_MESSAGE));
            }

            if (!value.Any(char.IsLower))
            {
                notifications.Add(Notification.Error(
                    code: PASSWORD_WITHOUT_LOWERCASE_NOTIFICATION_CODE,
                    message: PASSWORD_WITHOUT_LOWERCASE_NOTIFICATION_MESSAGE));
            }

            if (!value.Any(char.IsAsciiDigit))
            {
                notifications.Add(Notification.Error(
                    code: PASSWORD_WITHOUT_NUMBER_NOTIFICATION_CODE,
                    message: PASSWORD_WITHOUT_NUMBER_NOTIFICATION_MESSAGE));
            }

            if (!value.Any(IsSymbol))
            {
                notifications.Add(Notification.Error(
                    code: PASSWORD_WITHOUT_SYMBOL_NOTIFICATION_CODE,
                    message: PASSWORD_WITHOUT_SYMBOL_NOTIFICATION_MESSAGE));
            }
        }

        if (notifications.HasAnyNotifications())
        {
            return new PasswordValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new PasswordValueObject(
            isValid: true,
            password: value,
            methodResult: MethodResult.Success());
    }

    private static bool IsSymbol(char character)
        => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character);

    public static implicit operator PasswordValueObject(string? obj)
        => Build(obj);
}
