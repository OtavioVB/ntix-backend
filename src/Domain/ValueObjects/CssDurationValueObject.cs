using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Duração de animação, em ms ou s.</summary>
public readonly partial struct CssDurationValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private CssDurationValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 16;

    private const string CSS_DURATION_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "CSS_DURATION_COULD_NOT_BE_EMPTY";
    private const string CSS_DURATION_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A duração não pode ser vazia.";

    private const string CSS_DURATION_FORMAT_INVALID_NOTIFICATION_CODE = "CSS_DURATION_FORMAT_INVALID";
    private const string CSS_DURATION_FORMAT_INVALID_NOTIFICATION_MESSAGE = "A duração deve ser um número em ms ou s, como 260ms.";

    public static CssDurationValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(CSS_DURATION_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, CSS_DURATION_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(CSS_DURATION_FORMAT_INVALID_NOTIFICATION_CODE, CSS_DURATION_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => DurationRegex().IsMatch(value);

    [GeneratedRegex(@"^\d*\.?\d+(ms|s)$")]
    private static partial Regex DurationRegex();

    private static CssDurationValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator CssDurationValueObject(string? obj)
        => Build(obj);
}
