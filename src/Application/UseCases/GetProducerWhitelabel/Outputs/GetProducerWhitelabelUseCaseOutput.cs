namespace Application.UseCases.GetProducerWhitelabel.Outputs;

/// <summary>
/// Configuração whitelabel já validada, no formato do contrato <c>WhitelabelConfig</c> do front.
/// Montada a partir da entidade, nunca do texto do arquivo. Campos nulos ficam fora da resposta.
/// </summary>
public sealed record GetProducerWhitelabelUseCaseOutput(
    int? Version,
    WhitelabelBrandOutput? Brand,
    WhitelabelColorsOutput? Colors,
    WhitelabelTypographyOutput? Typography,
    WhitelabelSpacingOutput? Spacing,
    IReadOnlyDictionary<string, string>? Radius,
    WhitelabelMotionOutput? Motion);

public sealed record WhitelabelBrandOutput(string? Name, string? LogoUrl, string? FaviconUrl, WhitelabelThemeColorOutput? ThemeColor);

public sealed record WhitelabelThemeColorOutput(string? Light, string? Dark);

public sealed record WhitelabelColorsOutput(
    IReadOnlyDictionary<string, string>? Primary,
    IReadOnlyDictionary<string, string>? Secondary,
    IReadOnlyDictionary<string, string>? Neutral,
    IReadOnlyDictionary<string, string>? Success,
    IReadOnlyDictionary<string, string>? Danger,
    WhitelabelActionOutput? Action);

public sealed record WhitelabelActionOutput(string? Base, string? Hover, string? Ink);

public sealed record WhitelabelTypographyOutput(
    WhitelabelFontFamilyOutput? FontFamily,
    IReadOnlyList<string>? Stylesheets,
    IReadOnlyDictionary<string, string>? Text,
    IReadOnlyDictionary<string, string>? Display);

public sealed record WhitelabelFontFamilyOutput(string? Sans, string? Mono);

public sealed record WhitelabelSpacingOutput(IReadOnlyDictionary<string, string>? Scale, string? Gutter, string? PageMax);

public sealed record WhitelabelMotionOutput(IReadOnlyDictionary<string, string>? Duration, WhitelabelEasingOutput? Easing);

public sealed record WhitelabelEasingOutput(string? Out, string? InOut);
