using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Nome da plataforma whitelabel, exibido no título e no rótulo da marca.</summary>
public readonly partial struct BrandNameValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private BrandNameValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 60;

    private const string BRAND_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "BRAND_NAME_COULD_NOT_BE_EMPTY";
    private const string BRAND_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O nome da marca não pode ser vazio.";

    private const string BRAND_NAME_FORMAT_INVALID_NOTIFICATION_CODE = "BRAND_NAME_FORMAT_INVALID";
    private const string BRAND_NAME_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O nome da marca deve ter até 60 caracteres, sem caracteres de controle.";

    public static BrandNameValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(BRAND_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, BRAND_NAME_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(BRAND_NAME_FORMAT_INVALID_NOTIFICATION_CODE, BRAND_NAME_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => !value.Any(char.IsControl);

    private static BrandNameValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator BrandNameValueObject(string? obj)
        => Build(obj);
}
