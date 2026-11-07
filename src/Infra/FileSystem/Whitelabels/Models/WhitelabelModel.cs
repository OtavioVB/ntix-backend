using Infra.FileSystem.Whitelabels.Enums;

namespace Infra.FileSystem.Whitelabels.Models;

/// <summary>Resultado da leitura do arquivo: o modelo só vem quando o status é <see cref="WhitelabelReadStatus.Found"/>.</summary>
public sealed record WhitelabelReadResult(WhitelabelReadStatus Status, WhitelabelModel? Model);

/// <summary>
/// O arquivo como foi lido, só com tipos primitivos. Ainda não é confiável: a Application valida cada valor.
/// Propriedades fora deste modelo já ficam de fora na leitura.
/// </summary>
public sealed record WhitelabelModel(
    int? Version,
    WhitelabelBrandModel? Brand,
    WhitelabelColorsModel? Colors,
    WhitelabelTypographyModel? Typography,
    WhitelabelSpacingModel? Spacing,
    Dictionary<string, string>? Radius,
    WhitelabelMotionModel? Motion);

public sealed record WhitelabelBrandModel(string? Name, string? LogoUrl, string? FaviconUrl, WhitelabelThemeColorModel? ThemeColor);

public sealed record WhitelabelThemeColorModel(string? Light, string? Dark);

public sealed record WhitelabelColorsModel(
    Dictionary<string, string>? Primary,
    Dictionary<string, string>? Secondary,
    Dictionary<string, string>? Neutral,
    Dictionary<string, string>? Success,
    Dictionary<string, string>? Danger,
    WhitelabelActionModel? Action);

public sealed record WhitelabelActionModel(string? Base, string? Hover, string? Ink);

public sealed record WhitelabelTypographyModel(
    WhitelabelFontFamilyModel? FontFamily,
    List<string>? Stylesheets,
    Dictionary<string, string>? Text,
    Dictionary<string, string>? Display);

public sealed record WhitelabelFontFamilyModel(string? Sans, string? Mono);

public sealed record WhitelabelSpacingModel(Dictionary<string, string>? Scale, string? Gutter, string? PageMax);

public sealed record WhitelabelMotionModel(Dictionary<string, string>? Duration, WhitelabelEasingModel? Easing);

public sealed record WhitelabelEasingModel(string? Out, string? InOut);
