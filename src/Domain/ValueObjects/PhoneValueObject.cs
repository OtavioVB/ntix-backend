using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>Telefone obrigatório, normalizado no formato E.164 (ex.: +5511999999999).</summary>
public readonly struct PhoneValueObject
{
    public bool IsValid { get; }

    private readonly string? _phone;
    public string Value => IsValid
        ? _phone!
        : throw new InvalidOperationException("It is not possible to get phone because value object is invalid");

    public MethodResult MethodResult { get; }

    private PhoneValueObject(bool isValid, MethodResult methodResult, string? phone = null)
    {
        IsValid = isValid;
        _phone = phone;
        MethodResult = methodResult;
    }

    /// <summary>"+" e até 15 dígitos, o limite do E.164.</summary>
    public const int MAX_LENGTH = 16;

    private const string BRAZIL_COUNTRY_CODE = "55";
    private const int E164_MIN_DIGITS = 8;
    private const int E164_MAX_DIGITS = 15;
    private const int NATIONAL_LANDLINE_DIGITS = 10;
    private const int NATIONAL_MOBILE_DIGITS = 11;

    private const string PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "PHONE_COULD_NOT_BE_EMPTY";
    private const string PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O telefone não pode ser vazio.";

    private const string PHONE_FORMAT_INVALID_NOTIFICATION_CODE = "PHONE_FORMAT_INVALID";
    private const string PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O telefone deve conter o DDD e o número, como (11) 98765-4321, ou estar no formato internacional, como +5511987654321.";

    public static PhoneValueObject Build(string? phone)
    {
        var trimmed = (phone ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, PHONE_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        var isInternational = trimmed.StartsWith('+');
        var digits = (isInternational ? trimmed[1..] : trimmed)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return Invalid(PHONE_FORMAT_INVALID_NOTIFICATION_CODE, PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        if (isInternational)
        {
            if (digits.Length < E164_MIN_DIGITS || digits.Length > E164_MAX_DIGITS || digits[0] == '0')
                return Invalid(PHONE_FORMAT_INVALID_NOTIFICATION_CODE, PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE);

            return Valid($"+{digits}");
        }

        if (digits.Length != NATIONAL_LANDLINE_DIGITS && digits.Length != NATIONAL_MOBILE_DIGITS)
            return Invalid(PHONE_FORMAT_INVALID_NOTIFICATION_CODE, PHONE_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return Valid($"+{BRAZIL_COUNTRY_CODE}{digits}");
    }

    private static PhoneValueObject Valid(string phone)
        => new(isValid: true, phone: phone, methodResult: MethodResult.Success());

    private static PhoneValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator PhoneValueObject(string? obj)
        => Build(obj);
}
