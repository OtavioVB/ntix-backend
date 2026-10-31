using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;

namespace Domain.ValueObjects;

public readonly struct CpfValueObject
{
    public bool IsValid { get; }

    private readonly string? _cpf;
    public string Value => IsValid
        ? _cpf!
        : throw new InvalidOperationException("It is not possible to get CPF because value object is invalid");

    public MethodResult MethodResult { get; }

    private CpfValueObject(bool isValid, MethodResult methodResult, string? cpf = null)
    {
        IsValid = isValid;
        _cpf = cpf;
        MethodResult = methodResult;
    }

    public const int LENGTH = 11;

    private const int BASE_LENGTH = 9;
    private const int CHECK_DIGIT_MODULUS = 11;

    private const string CPF_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "CPF_COULD_NOT_BE_EMPTY";
    private const string CPF_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O CPF não pode ser vazio.";

    private const string CPF_FORMAT_INVALID_NOTIFICATION_CODE = "CPF_FORMAT_INVALID";
    private const string CPF_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O CPF informado não é válido.";

    public static CpfValueObject Build(string? cpf)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 1;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        var trimmed = (cpf ?? string.Empty).Trim();
        var digits = trimmed.Replace(".", string.Empty).Replace("-", string.Empty);

        if (trimmed.Length == 0)
        {
            notifications.Add(Notification.Error(
                code: CPF_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: CPF_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE));
        }
        else if (!HasValidDigits(digits))
        {
            notifications.Add(Notification.Error(
                code: CPF_FORMAT_INVALID_NOTIFICATION_CODE,
                message: CPF_FORMAT_INVALID_NOTIFICATION_MESSAGE));
        }

        if (notifications.HasAnyNotifications())
        {
            return new CpfValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new CpfValueObject(
            isValid: true,
            cpf: digits,
            methodResult: MethodResult.Success());
    }

    public string GetObfuscatedValue()
    {
        var cpf = Value;

        return $"***.{cpf[3..6]}.{cpf[6..9]}-**";
    }

    private static bool HasValidDigits(string digits)
    {
        if (digits.Length != LENGTH || !digits.All(char.IsAsciiDigit))
            return false;

        if (digits.All(digit => digit == digits[0]))
            return false;

        return CalculateCheckDigit(digits, BASE_LENGTH) == digits[BASE_LENGTH] - '0'
            && CalculateCheckDigit(digits, BASE_LENGTH + 1) == digits[BASE_LENGTH + 1] - '0';
    }

    private static int CalculateCheckDigit(string digits, int length)
    {
        var sum = 0;

        for (var i = 0; i < length; i++)
            sum += (digits[i] - '0') * (length + 1 - i);

        var remainder = sum % CHECK_DIGIT_MODULUS;

        return remainder < 2 ? 0 : CHECK_DIGIT_MODULUS - remainder;
    }

    public static implicit operator CpfValueObject(string? obj)
        => Build(obj);
}
