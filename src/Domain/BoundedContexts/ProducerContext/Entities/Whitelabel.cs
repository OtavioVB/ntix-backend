using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

/// <summary>
/// Identidade visual de uma produtora (whitelabel). Espelha o contrato <c>WhitelabelConfig</c> do front
/// (<c>web/src/whitelabel/config.ts</c>). Tudo é opcional: o que faltar, o front preenche com o padrão Ntix.
/// </summary>
public sealed record Whitelabel(
    int? Version,
    WhitelabelBrand? Brand,
    WhitelabelColors? Colors,
    WhitelabelTypography? Typography,
    WhitelabelSpacing? Spacing,
    IReadOnlyDictionary<string, CssLengthValueObject>? Radius,
    WhitelabelMotion? Motion)
{
    /// <summary>Única versão do contrato suportada.</summary>
    public const int CURRENT_VERSION = 1;

    // Chaves aceitas em cada escala. Chave fora destas listas é descartada, nunca devolvida.
    public static readonly IReadOnlySet<string> COLOR_STEPS = new HashSet<string> { "0", "50", "100", "200", "300", "400", "500", "600", "700", "800", "850", "900", "950" };
    public static readonly IReadOnlySet<string> TEXT_SIZES = new HashSet<string> { "xs", "sm", "md", "lg", "xl" };
    public static readonly IReadOnlySet<string> DISPLAY_SIZES = new HashSet<string> { "sm", "md", "lg" };
    public static readonly IReadOnlySet<string> SPACING_STEPS = new HashSet<string> { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
    public static readonly IReadOnlySet<string> RADIUS_SIZES = new HashSet<string> { "sm", "md", "lg", "notch" };
    public static readonly IReadOnlySet<string> DURATIONS = new HashSet<string> { "fast", "base", "slow", "scene" };

    /// <summary>Limite de folhas de estilo de fonte, para a página não carregar recursos sem fim.</summary>
    public const int STYLESHEETS_MAX_COUNT = 10;
}

public sealed record WhitelabelBrand(BrandNameValueObject? Name, AssetUrlValueObject? LogoUrl, AssetUrlValueObject? FaviconUrl, WhitelabelThemeColor? ThemeColor);

public sealed record WhitelabelThemeColor(ColorValueObject? Light, ColorValueObject? Dark);

public sealed record WhitelabelColors(
    IReadOnlyDictionary<string, ColorValueObject>? Primary,
    IReadOnlyDictionary<string, ColorValueObject>? Secondary,
    IReadOnlyDictionary<string, ColorValueObject>? Neutral,
    IReadOnlyDictionary<string, ColorValueObject>? Success,
    IReadOnlyDictionary<string, ColorValueObject>? Danger,
    WhitelabelAction? Action);

public sealed record WhitelabelAction(ColorValueObject? Base, ColorValueObject? Hover, ColorValueObject? Ink);

public sealed record WhitelabelTypography(
    WhitelabelFontFamily? FontFamily,
    IReadOnlyList<AssetUrlValueObject>? Stylesheets,
    IReadOnlyDictionary<string, CssLengthValueObject>? Text,
    IReadOnlyDictionary<string, CssLengthValueObject>? Display);

public sealed record WhitelabelFontFamily(FontFamilyValueObject? Sans, FontFamilyValueObject? Mono);

public sealed record WhitelabelSpacing(IReadOnlyDictionary<string, CssLengthValueObject>? Scale, CssLengthValueObject? Gutter, CssLengthValueObject? PageMax);

public sealed record WhitelabelMotion(IReadOnlyDictionary<string, CssDurationValueObject>? Duration, WhitelabelEasing? Easing);

public sealed record WhitelabelEasing(CssEasingValueObject? Out, CssEasingValueObject? InOut);
