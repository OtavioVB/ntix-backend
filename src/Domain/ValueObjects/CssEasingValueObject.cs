using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Curva de animação: cubic-bezier com quatro números ou palavra-chave CSS.</summary>
public readonly partial struct CssEasingValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private CssEasingValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 80;

    private const string CSS_EASING_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "CSS_EASING_COULD_NOT_BE_EMPTY";
    private const string CSS_EASING_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A curva não pode ser vazia.";

    private const string CSS_EASING_FORMAT_INVALID_NOTIFICATION_CODE = "CSS_EASING_FORMAT_INVALID";
    private const string CSS_EASING_FORMAT_INVALID_NOTIFICATION_MESSAGE = "A curva deve ser cubic-bezier(x1, y1, x2, y2) ou ease, ease-in, ease-out, ease-in-out ou linear.";

    public static CssEasingValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(CSS_EASING_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, CSS_EASING_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(CSS_EASING_FORMAT_INVALID_NOTIFICATION_CODE, CSS_EASING_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => KEYWORDS.Contains(value) || CubicBezierRegex().IsMatch(value);

    private static readonly HashSet<string> KEYWORDS = ["ease", "ease-in", "ease-out", "ease-in-out", "linear"];

    [GeneratedRegex(@"^cubic-bezier\(\s*-?\d*\.?\d+\s*(,\s*-?\d*\.?\d+\s*){3}\)$")]
    private static partial Regex CubicBezierRegex();

    private static CssEasingValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator CssEasingValueObject(string? obj)
        => Build(obj);
}
