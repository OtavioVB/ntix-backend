using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Medida do design system (texto, espaço, raio): número com unidade, ou clamp()/calc()/min()/max() compostos só por medidas.</summary>
public readonly partial struct CssLengthValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private CssLengthValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 120;

    private const string CSS_LENGTH_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "CSS_LENGTH_COULD_NOT_BE_EMPTY";
    private const string CSS_LENGTH_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A medida não pode ser vazia.";

    private const string CSS_LENGTH_FORMAT_INVALID_NOTIFICATION_CODE = "CSS_LENGTH_FORMAT_INVALID";
    private const string CSS_LENGTH_FORMAT_INVALID_NOTIFICATION_MESSAGE = "A medida deve ser um número com unidade (px, rem, em, vw, vh, %), ou clamp(), calc(), min() ou max() compostos só por medidas.";

    public static CssLengthValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(CSS_LENGTH_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, CSS_LENGTH_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(CSS_LENGTH_FORMAT_INVALID_NOTIFICATION_CODE, CSS_LENGTH_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => LengthRegex().IsMatch(value) || FunctionRegex().IsMatch(value);

    /// <summary>Número com unidade, ou zero sem unidade.</summary>
    [GeneratedRegex(@"^(0|-?\d*\.?\d+(px|rem|em|vw|vh|%))$")]
    private static partial Regex LengthRegex();

    /// <summary>Função de medida cujo conteúdo só tem números, unidades, operadores, vírgulas e espaços.</summary>
    [GeneratedRegex(@"^(clamp|calc|min|max)\((\s*(-?\d*\.?\d+(px|rem|em|vw|vh|%)?|[+\-*/,]))+\s*\)$")]
    private static partial Regex FunctionRegex();

    private static CssLengthValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator CssLengthValueObject(string? obj)
        => Build(obj);
}
