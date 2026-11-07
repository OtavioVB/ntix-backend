using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Text.RegularExpressions;

namespace Domain.ValueObjects;

/// <summary>Endereço de recurso da marca (logo, favicon, fonte): https absoluto ou caminho do próprio front.</summary>
public readonly partial struct AssetUrlValueObject
{
    public bool IsValid { get; }

    private readonly string? _value;
    public string Value => IsValid
        ? _value!
        : throw new InvalidOperationException("It is not possible to get value because value object is invalid");

    public MethodResult MethodResult { get; }

    private AssetUrlValueObject(bool isValid, MethodResult methodResult, string? value = null)
    {
        IsValid = isValid;
        _value = value;
        MethodResult = methodResult;
    }

    public const int MAX_LENGTH = 2048;

    private const string ASSET_URL_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE = "ASSET_URL_COULD_NOT_BE_EMPTY";
    private const string ASSET_URL_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE = "O endereço não pode ser vazio.";

    private const string ASSET_URL_FORMAT_INVALID_NOTIFICATION_CODE = "ASSET_URL_FORMAT_INVALID";
    private const string ASSET_URL_FORMAT_INVALID_NOTIFICATION_MESSAGE = "O endereço deve começar com https:// ou ser um caminho do próprio site, começando com /.";

    public static AssetUrlValueObject Build(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Invalid(ASSET_URL_COULD_NOT_BE_EMPTY_NOTIFICATION_CODE, ASSET_URL_COULD_NOT_BE_EMPTY_NOTIFICATION_MESSAGE);

        if (trimmed.Length > MAX_LENGTH || !IsWellFormed(trimmed))
            return Invalid(ASSET_URL_FORMAT_INVALID_NOTIFICATION_CODE, ASSET_URL_FORMAT_INVALID_NOTIFICATION_MESSAGE);

        return new(isValid: true, value: trimmed, methodResult: MethodResult.Success());
    }

    private static bool IsWellFormed(string value)
    {
        if (value.Any(char.IsControl) || value.Any(char.IsWhiteSpace))
            return false;

        if (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\'))
            return true;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static AssetUrlValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    public static implicit operator AssetUrlValueObject(string? obj)
        => Build(obj);
}
