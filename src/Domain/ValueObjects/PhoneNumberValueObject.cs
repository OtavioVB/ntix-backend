using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

public readonly struct PhoneNumberValueObject
{
    public bool IsValid { get; }

    private readonly string? _phoneNumber;

    // Opcional: nulo quando o cliente não informou o celular.
    public string? Value => IsValid
        ? _phoneNumber
        : throw new InvalidOperationException("It is not possible to get phone number because value object is invalid");

    public MethodResult MethodResult { get; }

    private PhoneNumberValueObject(bool isValid, MethodResult methodResult, string? phoneNumber = null)
    {
        IsValid = isValid;
        _phoneNumber = phoneNumber;
        MethodResult = methodResult;
    }

    public const int LENGTH = 11;

    private const string PHONE_NUMBER_FORMAT_INVALID_NOTIFICATION_CODE = "PHONE_NUMBER_FORMAT_INVALID";
    private const string PHONE_NUMBER_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O celular deve conter o DDD e o número, como (11) 98765-4321.";

    public static PhoneNumberValueObject Build(string? phoneNumber)
    {
        var trimmed = (phoneNumber ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return new PhoneNumberValueObject(
                isValid: true,
                methodResult: MethodResult.Success());
        }

        var digits = trimmed
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        if (digits.Length != LENGTH || !digits.All(char.IsAsciiDigit))
        {
            var notification = Notification.Error(
                code: PHONE_NUMBER_FORMAT_INVALID_NOTIFICATION_CODE,
                message: PHONE_NUMBER_FORMAT_INVALID_NOTIFICATION_MESSAGE);

            return new PhoneNumberValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new PhoneNumberValueObject(
            isValid: true,
            phoneNumber: digits,
            methodResult: MethodResult.Success());
    }

    public static implicit operator PhoneNumberValueObject(string? obj)
        => Build(obj);
}
