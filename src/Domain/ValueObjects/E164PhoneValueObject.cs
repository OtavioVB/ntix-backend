using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

public readonly struct E164PhoneValueObject
{
    public bool IsValid { get; }

    private readonly string? _phone;
    public string Value => IsValid
        ? _phone!
        : throw new InvalidOperationException("It is not possible to get phone because value object is invalid");

    public MethodResult MethodResult { get; }

    private E164PhoneValueObject(bool isValid, MethodResult methodResult, string? phone = null)
    {
        IsValid = isValid;
        _phone = phone;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 16;

    private const int MIN_DIGITS = 8;
    private const int MAX_DIGITS = 15;
    private const char PREFIX = '+';

    private const string PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "PHONE_COULD_NOT_BE_EMPTY";
    private const string PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O telefone não pode ser vazio.";

    private const string PHONE_FORMAT_INVALID_NOTIFICATION_CODE = "PHONE_FORMAT_INVALID";
    private const string PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O telefone deve conter o código do país, o DDD e o número, como +5511999999999.";

    public static E164PhoneValueObject Build(string? phone)
    {
        var normalized = (phone ?? string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        if (normalized.Length == 0)
        {
            return BuildInvalid(
                code: PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE,
                message: PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);
        }

        if (!HasValidFormat(normalized))
        {
            return BuildInvalid(
                code: PHONE_FORMAT_INVALID_NOTIFICATION_CODE,
                message: PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE);
        }

        return new E164PhoneValueObject(
            isValid: true,
            phone: normalized,
            methodResult: MethodResult.Success());
    }

    private static E164PhoneValueObject BuildInvalid(string code, string message)
        => new(
            isValid: false,
            methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    private static bool HasValidFormat(string phone)
    {
        var digits = phone.AsSpan(1);

        return phone[0] == PREFIX
            && digits.Length is >= MIN_DIGITS and <= MAX_DIGITS
            && digits[0] != '0'
            && !digits.ContainsAnyExceptInRange('0', '9');
    }

    public static implicit operator E164PhoneValueObject(string? obj)
        => Build(obj);
}
