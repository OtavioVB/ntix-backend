using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Cor da identidade visual, em hexadecimal #rrggbb.</summary>
public readonly partial struct ColorValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private ColorValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 7;

    private const string COLOR_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "COLOR_COULD_NOT_BE_EMPTY";
    private const string COLOR_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A cor não pode ser vazia.";

    private const string COLOR_FORMAT_INVALID_NOTIFICATION_CODE = "COLOR_FORMAT_INVALID";
    private const string COLOR_FORMAT_INVALID_NOTIFICATION_MESSAGE = "A cor deve estar no formato hexadecimal #rrggbb, como #c99b14.";

    public static ColorValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(COLOR_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, COLOR_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(COLOR_FORMAT_INVALID_NOTIFICATION_CODE, COLOR_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => HexColorRegex().IsMatch(value);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

    private static ColorValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator ColorValueObject(string? obj)
        => Build(obj);
}
