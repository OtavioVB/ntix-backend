using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Domain.ValueObjects;

public readonly struct SlugValueObject
{
    public bool IsValid { get; }

    private readonly string? _baseSlug;
    private readonly string? _slug;
    public string Value => IsValid
        ? _slug!
        : throw new InvalidOperationException("It is not possible to get slug because value object is invalid");

    public MethodResult MethodResult { get; }

    private SlugValueObject(bool isValid, MethodResult methodResult, string? baseSlug = null, string? slug = null)
    {
        IsValid = isValid;
        _baseSlug = baseSlug;
        _slug = slug;
        MethodResult = methodResult;
    }

    public const int RANDOM_VERSION_LENGTH = 5;

    private const string RANDOM_VERSION_CHARACTERS = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const char SEPARATOR = '-';

    private const string SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_CODE = "SLUG_COULD_NOT_BE_GENERATED";
    private const string SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_MESSAGE = "Não foi possível gerar o slug: informe ao menos uma letra ou número.";

    public static SlugValueObject Build(string? source)
    {
        var baseSlug = Normalize(source);

        if (baseSlug.Length == 0)
        {
            var notification = Notification.Error(
                code: SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_CODE,
                message: SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_MESSAGE);

            return new SlugValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new SlugValueObject(
            isValid: true,
            baseSlug: baseSlug,
            slug: baseSlug,
            methodResult: MethodResult.Success());
    }

    public SlugValueObject BuildRandomVersion()
    {
        if (!IsValid)
            throw new InvalidOperationException("It is not possible to build a slug version because value object is invalid");

        var version = RandomNumberGenerator.GetString(RANDOM_VERSION_CHARACTERS, RANDOM_VERSION_LENGTH);

        return new SlugValueObject(
            isValid: true,
            baseSlug: _baseSlug,
            slug: $"{_baseSlug}{SEPARATOR}{version}",
            methodResult: MethodResult.Success());
    }

    private static string Normalize(string? source)
    {
        var decomposed = (source ?? string.Empty).Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            var lowered = char.ToLowerInvariant(character);

            if (char.IsAsciiLetterOrDigit(lowered))
                slug.Append(lowered);
            else if ((char.IsWhiteSpace(character) || character == SEPARATOR) && slug.Length > 0 && slug[^1] != SEPARATOR)
                slug.Append(SEPARATOR);
        }

        return slug.ToString().TrimEnd(SEPARATOR);
    }
}
