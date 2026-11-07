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
    private readonly int? _maxLength;

    public string Value => IsValid
        ? _slug!
        : throw new InvalidOperationException("It is not possible to get slug because value object is invalid");

    public MethodResult MethodResult { get; }

    /// <summary>Palavra da plataforma que não pode virar subdomínio de produtor.</summary>
    public bool IsReservedSubdomain => IsValid && RESERVED_SUBDOMAINS.Contains(_slug!);

    private SlugValueObject(bool isValid, MethodResult methodResult, string? baseSlug = null, string? slug = null, int? maxLength = null)
    {
        IsValid = isValid;
        _baseSlug = baseSlug;
        _slug = slug;
        _maxLength = maxLength;
        MethodResult = methodResult;
    }

    public const int RANDOM_VERSION_LENGTH = 5;

    /// <summary>Limite de um rótulo de subdomínio.</summary>
    public const int SUBDOMAIN_MAX_LENGTH = 63;

    private static readonly HashSet<string> RESERVED_SUBDOMAINS = ["www", "api", "admin", "app", "hub", "produtores"];

    private const string RANDOM_VERSION_CHARACTERS = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const char SEPARATOR = '-';

    private const string SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_CODE = "SLUG_COULD_NOT_BE_GENERATED";
    private const string SLUG_COULD_NOT_BE_GENERATED_NOTIFICATION_MESSAGE = "Não foi possível gerar o slug: informe ao menos uma letra ou número.";

    /// <param name="maxLength">Tamanho máximo do slug, contando a versão aleatória. Nulo quando não há limite.</param>
    public static SlugValueObject Build(string? source, int? maxLength = null)
    {
        var baseSlug = Truncate(Normalize(source), maxLength);

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
            maxLength: maxLength,
            methodResult: MethodResult.Success());
    }

    private const string SLUG_INVALID_NOTIFICATION_CODE = "SLUG_INVALID";
    private const string SLUG_INVALID_NOTIFICATION_MESSAGE = "O slug informado é inválido: use só letras minúsculas, números e hífen, com até 63 caracteres.";

    /// <summary>
    /// Recebe um slug já existente (rota, subdomínio), sem normalizar: só é válido se já estiver na forma
    /// canônica de subdomínio. Nada de "../", barra, ponto ou maiúscula passa, então o valor é seguro para compor caminhos.
    /// </summary>
    public static SlugValueObject Parse(string? slug)
    {
        var canonical = Truncate(Normalize(slug), SUBDOMAIN_MAX_LENGTH);

        if (string.IsNullOrEmpty(slug) || canonical != slug)
        {
            var notification = Notification.Error(
                code: SLUG_INVALID_NOTIFICATION_CODE,
                message: SLUG_INVALID_NOTIFICATION_MESSAGE);

            return new SlugValueObject(
                isValid: false,
                methodResult: MethodResult.Error([notification]));
        }

        return new SlugValueObject(
            isValid: true,
            baseSlug: slug,
            slug: slug,
            maxLength: SUBDOMAIN_MAX_LENGTH,
            methodResult: MethodResult.Success());
    }

    public SlugValueObject BuildRandomVersion()
    {
        if (!IsValid)
            throw new InvalidOperationException("It is not possible to build a slug version because value object is invalid");

        var version = RandomNumberGenerator.GetString(RANDOM_VERSION_CHARACTERS, RANDOM_VERSION_LENGTH);

        // A versão conta no limite: o slug base é cortado para caber o separador e as 5 letras.
        var prefix = Truncate(_baseSlug!, _maxLength - RANDOM_VERSION_LENGTH - 1);

        return new SlugValueObject(
            isValid: true,
            baseSlug: _baseSlug,
            slug: prefix.Length == 0 ? version : $"{prefix}{SEPARATOR}{version}",
            maxLength: _maxLength,
            methodResult: MethodResult.Success());
    }

    private static string Truncate(string slug, int? maxLength)
        => maxLength is null || slug.Length <= maxLength
            ? slug
            : slug[..Math.Max(maxLength.Value, 0)].TrimEnd(SEPARATOR);

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
