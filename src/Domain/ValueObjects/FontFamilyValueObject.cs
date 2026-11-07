using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Pilha de fontes: nomes entre aspas ou simples, separados por vírgula, sem nada que abra outra regra CSS ou carregue recurso.</summary>
public readonly partial struct FontFamilyValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private FontFamilyValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 200;

    private const string FONT_FAMILY_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "FONT_FAMILY_COULD_NOT_BE_EMPTY";
    private const string FONT_FAMILY_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "A fonte não pode ser vazia.";

    private const string FONT_FAMILY_FORMAT_INVALID_NOTIFICATION_CODE = "FONT_FAMILY_FORMAT_INVALID";
    private const string FONT_FAMILY_FORMAT_INVALID_NOTIFICATION_MESSAGE = "A fonte deve ser uma lista de nomes separados por vírgula, como \"Inter\", system-ui, sans-serif.";

    public static FontFamilyValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(FONT_FAMILY_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, FONT_FAMILY_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(FONT_FAMILY_FORMAT_INVALID_NOTIFICATION_CODE, FONT_FAMILY_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
        => FontFamilyRegex().IsMatch(value);

    /// <summary>
    /// Cada item: nome entre aspas duplas ou simples, ou palavras com letras, números, espaço e hífen
    /// (com hífen inicial opcional, como -apple-system).
    /// </summary>
    [GeneratedRegex(@"^(""[A-Za-z0-9 \-]+""|'[A-Za-z0-9 \-]+'|-?[A-Za-z][A-Za-z0-9 \-]*)(\s*,\s*(""[A-Za-z0-9 \-]+""|'[A-Za-z0-9 \-]+'|-?[A-Za-z][A-Za-z0-9 \-]*))*$")]
    private static partial Regex FontFamilyRegex();

    private static FontFamilyValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator FontFamilyValueObject(string? obj)
        => Build(obj);
}
