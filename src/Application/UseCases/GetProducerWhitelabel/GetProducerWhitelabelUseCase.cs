using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetProducerWhitelabel.Inputs;
using Application.UseCases.GetProducerWhitelabel.Outputs;
using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.FileSystem.Whitelabels.Enums;
using Infra.FileSystem.Whitelabels.Interfaces;
using Infra.FileSystem.Whitelabels.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetProducerWhitelabel;

/// <summary>
/// Consulta a configuração whitelabel da produtora. O arquivo nunca é devolvido como está: a cada consulta
/// ele é lido, validado campo a campo contra a estrutura e a resposta é montada a partir da entidade validada.
/// </summary>
public sealed class GetProducerWhitelabelUseCase : IUseCase<GetProducerWhitelabelUseCaseInput, GetProducerWhitelabelUseCaseOutput>
{
    private readonly IWhitelabelReader _whitelabelReader;
    private readonly GetProducerWhitelabelDiagnostics _diagnostics;
    private readonly ILogger<GetProducerWhitelabelUseCase> _logger;

    public GetProducerWhitelabelUseCase(
        IWhitelabelReader whitelabelReader,
        GetProducerWhitelabelDiagnostics diagnostics,
        ILogger<GetProducerWhitelabelUseCase> logger)
    {
        _whitelabelReader = whitelabelReader;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string WHITELABEL_NOT_FOUND_NOTIFICATION_CODE = "WHITELABEL_NOT_FOUND";
    private const string WHITELABEL_NOT_FOUND_NOTIFICATION_MESSAGE = "Não existe configuração de whitelabel para a produtora informada.";

    public const string WHITELABEL_INVALID_FILE_NOTIFICATION_CODE = "WHITELABEL_INVALID_FILE";
    private const string WHITELABEL_INVALID_FILE_NOTIFICATION_MESSAGE = "A configuração de whitelabel da produtora não está disponível.";

    public const string GET_PRODUCER_WHITELABEL_CANCELED_NOTIFICATION_CODE = "GET_PRODUCER_WHITELABEL_CANCELED";
    private const string GET_PRODUCER_WHITELABEL_CANCELED_NOTIFICATION_MESSAGE = "A consulta do whitelabel foi cancelada.";

    public async Task<MethodResult<GetProducerWhitelabelUseCaseOutput>> ExecuteAsync(GetProducerWhitelabelUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = StartActivity(auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(validationResult);

            var slug = input.Slug.Value;
            var readResult = await _whitelabelReader.ReadAsync(slug, cancellationToken);

            if (readResult.Status == WhitelabelReadStatus.NotFound)
                return BuildNotFoundResult(slug);

            if (readResult.Status != WhitelabelReadStatus.Found)
                return BuildInvalidFileResult(slug, readResult.Status, invalidFields: []);

            var invalidFields = new List<string>();
            var whitelabel = BuildWhitelabel(readResult.Model!, invalidFields);

            // Uma marca pela metade não é servida: qualquer campo fora do formato invalida o arquivo inteiro.
            if (invalidFields.Count > 0)
                return BuildInvalidFileResult(slug, readResult.Status, invalidFields);

            return BuildSuccessResult(slug, whitelabel);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return BuildCanceledResult();

            GetProducerWhitelabelLog.Failed(_logger, input.Slug.IsValid ? input.Slug.Value : string.Empty, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetProducerWhitelabelDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private static Activity? StartActivity(AuditableInfoValueObject auditableInfo)
    {
        var activity = GetProducerWhitelabelDiagnostics.ACTIVITY_SOURCE.StartActivity("GetProducerWhitelabel");

        if (activity is not null && auditableInfo.IsValid)
        {
            activity.SetTag("ntix.correlation_id", auditableInfo.CorrelationId);
            activity.SetTag("ntix.origin_service", auditableInfo.OriginService);
            activity.SetTag("ntix.execution_user", auditableInfo.ExecutionUser);
            activity.SetTag("ntix.requested_at", auditableInfo.RequestedAt);
        }

        return activity;
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.Finished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    #region Arquivo -> entidade (validação campo a campo)

    private static Whitelabel BuildWhitelabel(WhitelabelModel model, List<string> invalidFields)
    {
        if (model.Version is not null && model.Version != Whitelabel.CURRENT_VERSION)
            invalidFields.Add("version");

        return new Whitelabel(
            Version: model.Version,
            Brand: model.Brand is null ? null : new WhitelabelBrand(
                Name: Optional(model.Brand.Name, BrandNameValueObject.Build, v => v.IsValid, v => v.MethodResult, "brand.name", invalidFields),
                LogoUrl: OptionalAsset(model.Brand.LogoUrl, "brand.logoUrl", invalidFields),
                FaviconUrl: OptionalAsset(model.Brand.FaviconUrl, "brand.faviconUrl", invalidFields),
                ThemeColor: model.Brand.ThemeColor is null ? null : new WhitelabelThemeColor(
                    Light: OptionalColor(model.Brand.ThemeColor.Light, "brand.themeColor.light", invalidFields),
                    Dark: OptionalColor(model.Brand.ThemeColor.Dark, "brand.themeColor.dark", invalidFields))),
            Colors: model.Colors is null ? null : new WhitelabelColors(
                Primary: ColorScale(model.Colors.Primary, "colors.primary", invalidFields),
                Secondary: ColorScale(model.Colors.Secondary, "colors.secondary", invalidFields),
                Neutral: ColorScale(model.Colors.Neutral, "colors.neutral", invalidFields),
                Success: ColorScale(model.Colors.Success, "colors.success", invalidFields),
                Danger: ColorScale(model.Colors.Danger, "colors.danger", invalidFields),
                Action: model.Colors.Action is null ? null : new WhitelabelAction(
                    Base: OptionalColor(model.Colors.Action.Base, "colors.action.base", invalidFields),
                    Hover: OptionalColor(model.Colors.Action.Hover, "colors.action.hover", invalidFields),
                    Ink: OptionalColor(model.Colors.Action.Ink, "colors.action.ink", invalidFields))),
            Typography: model.Typography is null ? null : new WhitelabelTypography(
                FontFamily: model.Typography.FontFamily is null ? null : new WhitelabelFontFamily(
                    Sans: Optional(model.Typography.FontFamily.Sans, FontFamilyValueObject.Build, v => v.IsValid, v => v.MethodResult, "typography.fontFamily.sans", invalidFields),
                    Mono: Optional(model.Typography.FontFamily.Mono, FontFamilyValueObject.Build, v => v.IsValid, v => v.MethodResult, "typography.fontFamily.mono", invalidFields)),
                Stylesheets: Stylesheets(model.Typography.Stylesheets, invalidFields),
                Text: LengthScale(model.Typography.Text, Whitelabel.TEXT_SIZES, "typography.text", invalidFields),
                Display: LengthScale(model.Typography.Display, Whitelabel.DISPLAY_SIZES, "typography.display", invalidFields)),
            Spacing: model.Spacing is null ? null : new WhitelabelSpacing(
                Scale: LengthScale(model.Spacing.Scale, Whitelabel.SPACING_STEPS, "spacing.scale", invalidFields),
                Gutter: OptionalLength(model.Spacing.Gutter, "spacing.gutter", invalidFields),
                PageMax: OptionalLength(model.Spacing.PageMax, "spacing.pageMax", invalidFields)),
            Radius: LengthScale(model.Radius, Whitelabel.RADIUS_SIZES, "radius", invalidFields),
            Motion: model.Motion is null ? null : new WhitelabelMotion(
                Duration: Scale(model.Motion.Duration, Whitelabel.DURATIONS, CssDurationValueObject.Build, v => v.IsValid, v => v.MethodResult, "motion.duration", invalidFields),
                Easing: model.Motion.Easing is null ? null : new WhitelabelEasing(
                    Out: Optional(model.Motion.Easing.Out, CssEasingValueObject.Build, v => v.IsValid, v => v.MethodResult, "motion.easing.out", invalidFields),
                    InOut: Optional(model.Motion.Easing.InOut, CssEasingValueObject.Build, v => v.IsValid, v => v.MethodResult, "motion.easing.inOut", invalidFields))));
    }

    /// <summary>Campo ausente fica ausente; campo presente precisa ser válido.</summary>
    private static T? Optional<T>(string? value, Func<string?, T> build, Func<T, bool> isValid, Func<T, MethodResult> methodResult, string field, List<string> invalidFields)
        where T : struct
    {
        if (value is null)
            return null;

        var valueObject = build(value);

        if (isValid(valueObject))
            return valueObject;

        invalidFields.Add($"{field} ({string.Join(", ", methodResult(valueObject).Notifications.Select(notification => notification.Code))})");

        return null;
    }

    private static ColorValueObject? OptionalColor(string? value, string field, List<string> invalidFields)
        => Optional(value, ColorValueObject.Build, v => v.IsValid, v => v.MethodResult, field, invalidFields);

    private static CssLengthValueObject? OptionalLength(string? value, string field, List<string> invalidFields)
        => Optional(value, CssLengthValueObject.Build, v => v.IsValid, v => v.MethodResult, field, invalidFields);

    /// <summary>URL vazia é tratada como ausente: é assim que o arquivo padrão diz "sem logo".</summary>
    private static AssetUrlValueObject? OptionalAsset(string? value, string field, List<string> invalidFields)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Optional(value, AssetUrlValueObject.Build, v => v.IsValid, v => v.MethodResult, field, invalidFields);

    /// <summary>Chave fora da escala é descartada; valor de chave conhecida precisa ser válido.</summary>
    private static IReadOnlyDictionary<string, T>? Scale<T>(Dictionary<string, string>? values, IReadOnlySet<string> allowedKeys, Func<string?, T> build, Func<T, bool> isValid, Func<T, MethodResult> methodResult, string field, List<string> invalidFields)
        where T : struct
    {
        if (values is null)
            return null;

        var scale = new Dictionary<string, T>();

        foreach (var (key, value) in values.Where(entry => allowedKeys.Contains(entry.Key)))
        {
            var valueObject = Optional(value, build, isValid, methodResult, $"{field}.{key}", invalidFields);

            if (valueObject is not null)
                scale[key] = valueObject.Value;
        }

        return scale;
    }

    private static IReadOnlyDictionary<string, ColorValueObject>? ColorScale(Dictionary<string, string>? values, string field, List<string> invalidFields)
        => Scale(values, Whitelabel.COLOR_STEPS, ColorValueObject.Build, v => v.IsValid, v => v.MethodResult, field, invalidFields);

    private static IReadOnlyDictionary<string, CssLengthValueObject>? LengthScale(Dictionary<string, string>? values, IReadOnlySet<string> allowedKeys, string field, List<string> invalidFields)
        => Scale(values, allowedKeys, CssLengthValueObject.Build, v => v.IsValid, v => v.MethodResult, field, invalidFields);

    private static IReadOnlyList<AssetUrlValueObject>? Stylesheets(List<string>? values, List<string> invalidFields)
    {
        if (values is null)
            return null;

        if (values.Count > Whitelabel.STYLESHEETS_MAX_COUNT)
        {
            invalidFields.Add($"typography.stylesheets (mais de {Whitelabel.STYLESHEETS_MAX_COUNT})");
            return null;
        }

        var stylesheets = new List<AssetUrlValueObject>();

        for (var index = 0; index < values.Count; index++)
        {
            var stylesheet = Optional(values[index], AssetUrlValueObject.Build, v => v.IsValid, v => v.MethodResult, $"typography.stylesheets[{index}]", invalidFields);

            if (stylesheet is not null)
                stylesheets.Add(stylesheet.Value);
        }

        return stylesheets;
    }

    #endregion

    #region Entidade -> saída

    private static GetProducerWhitelabelUseCaseOutput BuildOutput(Whitelabel whitelabel)
        => new(
            Version: whitelabel.Version,
            Brand: whitelabel.Brand is null ? null : new WhitelabelBrandOutput(
                Name: whitelabel.Brand.Name?.Value,
                LogoUrl: whitelabel.Brand.LogoUrl?.Value,
                FaviconUrl: whitelabel.Brand.FaviconUrl?.Value,
                ThemeColor: whitelabel.Brand.ThemeColor is null ? null : new WhitelabelThemeColorOutput(
                    Light: whitelabel.Brand.ThemeColor.Light?.Value,
                    Dark: whitelabel.Brand.ThemeColor.Dark?.Value)),
            Colors: whitelabel.Colors is null ? null : new WhitelabelColorsOutput(
                Primary: Values(whitelabel.Colors.Primary, color => color.Value),
                Secondary: Values(whitelabel.Colors.Secondary, color => color.Value),
                Neutral: Values(whitelabel.Colors.Neutral, color => color.Value),
                Success: Values(whitelabel.Colors.Success, color => color.Value),
                Danger: Values(whitelabel.Colors.Danger, color => color.Value),
                Action: whitelabel.Colors.Action is null ? null : new WhitelabelActionOutput(
                    Base: whitelabel.Colors.Action.Base?.Value,
                    Hover: whitelabel.Colors.Action.Hover?.Value,
                    Ink: whitelabel.Colors.Action.Ink?.Value)),
            Typography: whitelabel.Typography is null ? null : new WhitelabelTypographyOutput(
                FontFamily: whitelabel.Typography.FontFamily is null ? null : new WhitelabelFontFamilyOutput(
                    Sans: whitelabel.Typography.FontFamily.Sans?.Value,
                    Mono: whitelabel.Typography.FontFamily.Mono?.Value),
                Stylesheets: whitelabel.Typography.Stylesheets?.Select(stylesheet => stylesheet.Value).ToArray(),
                Text: Values(whitelabel.Typography.Text, length => length.Value),
                Display: Values(whitelabel.Typography.Display, length => length.Value)),
            Spacing: whitelabel.Spacing is null ? null : new WhitelabelSpacingOutput(
                Scale: Values(whitelabel.Spacing.Scale, length => length.Value),
                Gutter: whitelabel.Spacing.Gutter?.Value,
                PageMax: whitelabel.Spacing.PageMax?.Value),
            Radius: Values(whitelabel.Radius, length => length.Value),
            Motion: whitelabel.Motion is null ? null : new WhitelabelMotionOutput(
                Duration: Values(whitelabel.Motion.Duration, duration => duration.Value),
                Easing: whitelabel.Motion.Easing is null ? null : new WhitelabelEasingOutput(
                    Out: whitelabel.Motion.Easing.Out?.Value,
                    InOut: whitelabel.Motion.Easing.InOut?.Value)));

    private static IReadOnlyDictionary<string, string>? Values<T>(IReadOnlyDictionary<string, T>? scale, Func<T, string> value)
        => scale?.ToDictionary(entry => entry.Key, entry => value(entry.Value));

    #endregion

    private MethodResult<GetProducerWhitelabelUseCaseOutput> BuildSuccessResult(string slug, Whitelabel whitelabel)
    {
        RecordOutcome(GetProducerWhitelabelDiagnostics.OUTCOME_SERVED);

        GetProducerWhitelabelLog.Served(_logger, slug);

        return MethodResult<GetProducerWhitelabelUseCaseOutput>.Success(BuildOutput(whitelabel));
    }

    private MethodResult<GetProducerWhitelabelUseCaseOutput> BuildInvalidDataResult(MethodResult validationResult)
    {
        RecordOutcome(GetProducerWhitelabelDiagnostics.OUTCOME_VALIDATION_FAILED);

        GetProducerWhitelabelLog.InvalidData(
            logger: _logger,
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<GetProducerWhitelabelUseCaseOutput>.Error(validationResult.Notifications);
    }

    private MethodResult<GetProducerWhitelabelUseCaseOutput> BuildNotFoundResult(string slug)
    {
        RecordOutcome(GetProducerWhitelabelDiagnostics.OUTCOME_NOT_FOUND);

        GetProducerWhitelabelLog.NotFound(_logger, slug);

        var notFoundNotification = Notification.Error(
            code: WHITELABEL_NOT_FOUND_NOTIFICATION_CODE,
            message: WHITELABEL_NOT_FOUND_NOTIFICATION_MESSAGE);

        return MethodResult<GetProducerWhitelabelUseCaseOutput>.Error([notFoundNotification]);
    }

    private MethodResult<GetProducerWhitelabelUseCaseOutput> BuildInvalidFileResult(string slug, WhitelabelReadStatus readStatus, IReadOnlyList<string> invalidFields)
    {
        RecordOutcome(GetProducerWhitelabelDiagnostics.OUTCOME_INVALID_FILE);

        GetProducerWhitelabelLog.InvalidFile(
            logger: _logger,
            slug: slug,
            readStatus: readStatus.ToString(),
            invalidFields: invalidFields.Count == 0 ? "-" : string.Join("; ", invalidFields));

        var invalidFileNotification = Notification.Error(
            code: WHITELABEL_INVALID_FILE_NOTIFICATION_CODE,
            message: WHITELABEL_INVALID_FILE_NOTIFICATION_MESSAGE);

        return MethodResult<GetProducerWhitelabelUseCaseOutput>.Error([invalidFileNotification]);
    }

    private static MethodResult<GetProducerWhitelabelUseCaseOutput> BuildCanceledResult()
    {
        var canceledNotification = Notification.Error(
            code: GET_PRODUCER_WHITELABEL_CANCELED_NOTIFICATION_CODE,
            message: GET_PRODUCER_WHITELABEL_CANCELED_NOTIFICATION_MESSAGE);

        return MethodResult<GetProducerWhitelabelUseCaseOutput>.Error([canceledNotification]);
    }
}
