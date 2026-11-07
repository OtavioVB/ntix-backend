using Application.UseCases.GetProducerWhitelabel;
using Application.UseCases.GetProducerWhitelabel.Inputs;
using Domain.ValueObjects;
using Infra.FileSystem.Whitelabels.Enums;
using Infra.FileSystem.Whitelabels.Interfaces;
using Infra.FileSystem.Whitelabels.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.GetProducerWhitelabel;

public sealed class GetProducerWhitelabelUseCaseTests : IDisposable
{
    private const string SLUG = "ntix";

    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "ntix-web";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private const string REQUESTS_INSTRUMENT = "ntix.producers.whitelabel.requests";
    private const string OUTCOMES_INSTRUMENT = "ntix.producers.whitelabel.outcomes";

    private readonly IWhitelabelReader _reader = Substitute.For<IWhitelabelReader>();
    private readonly CapturingLogger<GetProducerWhitelabelUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly GetProducerWhitelabelUseCase _useCase;

    public GetProducerWhitelabelUseCaseTests()
    {
        var diagnostics = new GetProducerWhitelabelDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new GetProducerWhitelabelUseCase(
            whitelabelReader: _reader,
            diagnostics: diagnostics,
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT);

    private static WhitelabelModel BuildValidModel()
        => new(
            Version: 1,
            Brand: new WhitelabelBrandModel(Name: "Ntix", LogoUrl: "", FaviconUrl: "/favicon.svg", ThemeColor: new WhitelabelThemeColorModel(Light: "#ffffff", Dark: "#1b1d20")),
            Colors: new WhitelabelColorsModel(
                Primary: new() { ["50"] = "#fbf6e3", ["500"] = "#c99b14" },
                Secondary: new() { ["800"] = "#2a2c31" },
                Neutral: new() { ["0"] = "#ffffff", ["950"] = "#1b1d20" },
                Success: new() { ["300"] = "#7fd1a0" },
                Danger: new() { ["700"] = "#a8322a" },
                Action: new WhitelabelActionModel(Base: "#c99b14", Hover: "#d9af28", Ink: null)),
            Typography: new WhitelabelTypographyModel(
                FontFamily: new WhitelabelFontFamilyModel(Sans: "\"Archivo Variable\", system-ui, -apple-system, sans-serif", Mono: "\"IBM Plex Mono\", monospace"),
                Stylesheets: ["https://fonts.googleapis.com/css2?family=Inter"],
                Text: new() { ["md"] = "1rem", ["lg"] = "clamp(1.0625rem, 1rem + 0.3vw, 1.25rem)" },
                Display: new() { ["lg"] = "clamp(3.25rem, 0.8rem + 6.1vw, 7.25rem)" }),
            Spacing: new WhitelabelSpacingModel(Scale: new() { ["1"] = "0.25rem", ["10"] = "clamp(5rem, 3rem + 8vw, 10rem)" }, Gutter: "clamp(1rem, 0.2rem + 3.4vw, 3rem)", PageMax: "90rem"),
            Radius: new() { ["sm"] = "4px", ["notch"] = "11px" },
            Motion: new WhitelabelMotionModel(Duration: new() { ["fast"] = "140ms" }, Easing: new WhitelabelEasingModel(Out: "cubic-bezier(0.16, 1, 0.3, 1)", InOut: "ease-in-out")));

    private void GivenFile(WhitelabelModel model)
        => _reader.ReadAsync(SLUG, Arg.Any<CancellationToken>()).Returns(new WhitelabelReadResult(WhitelabelReadStatus.Found, model));

    private void GivenStatus(WhitelabelReadStatus status)
        => _reader.ReadAsync(SLUG, Arg.Any<CancellationToken>()).Returns(new WhitelabelReadResult(status, Model: null));

    [Fact]
    public async Task Given_ValidFile_When_Executing_Should_ReturnEveryFieldOfTheFile()
    {
        // Arrange
        GivenFile(BuildValidModel());

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        var output = result.Output!;
        Assert.Equal(1, output.Version);
        Assert.Equal("Ntix", output.Brand!.Name);
        Assert.Equal("/favicon.svg", output.Brand.FaviconUrl);
        Assert.Equal("#1b1d20", output.Brand.ThemeColor!.Dark);
        Assert.Equal("#c99b14", output.Colors!.Primary!["500"]);
        Assert.Equal("#2a2c31", output.Colors.Secondary!["800"]);
        Assert.Equal("#ffffff", output.Colors.Neutral!["0"]);
        Assert.Equal("#d9af28", output.Colors.Action!.Hover);
        Assert.Equal("\"IBM Plex Mono\", monospace", output.Typography!.FontFamily!.Mono);
        Assert.Equal(["https://fonts.googleapis.com/css2?family=Inter"], output.Typography.Stylesheets!);
        Assert.Equal("clamp(1.0625rem, 1rem + 0.3vw, 1.25rem)", output.Typography.Text!["lg"]);
        Assert.Equal("90rem", output.Spacing!.PageMax);
        Assert.Equal("11px", output.Radius!["notch"]);
        Assert.Equal("ease-in-out", output.Motion!.Easing!.InOut);
    }

    [Fact]
    public async Task Given_EmptyLogoUrl_When_Executing_Should_TreatLogoAsAbsent()
    {
        // Arrange
        GivenFile(BuildValidModel());

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Output!.Brand!.LogoUrl);
        Assert.Null(result.Output.Colors!.Action!.Ink);
    }

    [Fact]
    public async Task Given_KeysOutsideTheContract_When_Executing_Should_DiscardThem()
    {
        // Arrange
        var model = BuildValidModel();
        model.Colors!.Primary!["internal"] = "#000000";
        model.Radius!["xl"] = "40px";
        GivenFile(model);

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Output!.Colors!.Primary!.ContainsKey("internal"));
        Assert.False(result.Output.Radius!.ContainsKey("xl"));
    }

    [Fact]
    public async Task Given_MinimalFile_When_Executing_Should_ReturnOnlyWhatExists()
    {
        // Arrange
        GivenFile(new WhitelabelModel(Version: null, Brand: null, Colors: new WhitelabelColorsModel(Primary: new() { ["500"] = "#0044ff" }, null, null, null, null, null), Typography: null, Spacing: null, Radius: null, Motion: null));

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Output!.Brand);
        Assert.Null(result.Output.Typography);
        Assert.Null(result.Output.Colors!.Secondary);
        Assert.Equal("#0044ff", result.Output.Colors.Primary!["500"]);
    }

    public static TheoryData<string, Func<WhitelabelModel, WhitelabelModel>> InvalidFields => new()
    {
        { "colors.primary.500", model => model with { Colors = model.Colors! with { Primary = new() { ["500"] = "red" } } } },
        { "brand.logoUrl", model => model with { Brand = model.Brand! with { LogoUrl = "javascript:alert(1)" } } },
        { "brand.faviconUrl", model => model with { Brand = model.Brand! with { FaviconUrl = "http://evil.com/x.ico" } } },
        { "typography.fontFamily.sans", model => model with { Typography = model.Typography! with { FontFamily = new WhitelabelFontFamilyModel(Sans: "x; } body { display: none", Mono: null) } } },
        { "typography.stylesheets[0]", model => model with { Typography = model.Typography! with { Stylesheets = ["//evil.com/x.css"] } } },
        { "spacing.gutter", model => model with { Spacing = model.Spacing! with { Gutter = "url(x)" } } },
        { "motion.duration.fast", model => model with { Motion = model.Motion! with { Duration = new() { ["fast"] = "140" } } } },
        { "version", model => model with { Version = 2 } },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task Given_FieldOutsideTheStructure_When_Executing_Should_RejectTheWholeFileAndLogTheField(string field, Func<WhitelabelModel, WhitelabelModel> corrupt)
    {
        // Arrange
        GivenFile(corrupt(BuildValidModel()));

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Null(result.Output);
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.WHITELABEL_INVALID_FILE_NOTIFICATION_CODE);
        Assert.Contains(_logger.Logs, log => log.EventId == 3004 && log.Level == LogLevel.Error && log.Message.Contains(field));
    }

    [Fact]
    public async Task Given_TooManyStylesheets_When_Executing_Should_RejectTheFile()
    {
        // Arrange
        var model = BuildValidModel();
        GivenFile(model with { Typography = model.Typography! with { Stylesheets = Enumerable.Repeat("/font.css", 11).ToList() } });

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.WHITELABEL_INVALID_FILE_NOTIFICATION_CODE);
    }

    [Theory]
    [InlineData(WhitelabelReadStatus.TooLarge)]
    [InlineData(WhitelabelReadStatus.Malformed)]
    public async Task Given_UnreadableFile_When_Executing_Should_ReturnInvalidFile(WhitelabelReadStatus status)
    {
        // Arrange
        GivenStatus(status);

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.WHITELABEL_INVALID_FILE_NOTIFICATION_CODE);
        Assert.Contains(_logger.Logs, log => log.EventId == 3004 && log.Message.Contains(status.ToString()));
    }

    [Fact]
    public async Task Given_NoFile_When_Executing_Should_ReturnNotFound()
    {
        // Arrange
        GivenStatus(WhitelabelReadStatus.NotFound);

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.WHITELABEL_NOT_FOUND_NOTIFICATION_CODE);
        Assert.Contains(_logger.Logs, log => log.EventId == 3003);
    }

    [Theory]
    [InlineData("../appsettings")]
    [InlineData("Ntix")]
    [InlineData("")]
    public async Task Given_InvalidSlug_When_Executing_Should_RejectWithoutReadingTheDisk(string slug)
    {
        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(slug), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "SLUG_INVALID");
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_RejectWithoutReadingTheDisk()
    {
        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _reader.ReadAsync(SLUG, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.GET_PRODUCER_WHITELABEL_CANCELED_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Executing_Should_LogAndRethrow()
    {
        // Arrange
        _reader.ReadAsync(SLUG, Arg.Any<CancellationToken>()).ThrowsAsync(new UnauthorizedAccessException());

        // Act
        var act = () => _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
        Assert.Contains(_logger.Logs, log => log.EventId == 3005 && log.Level == LogLevel.Error);
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == OUTCOMES_INSTRUMENT && measurement.Outcome == GetProducerWhitelabelDiagnostics.OUTCOME_ERROR && measurement.Value == 1);
    }

    [Fact]
    public async Task Given_ServedConfiguration_When_Executing_Should_RecordFunnelAndAuditableScope()
    {
        // Arrange
        GivenFile(BuildValidModel());

        // Act
        await _useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == REQUESTS_INSTRUMENT && measurement.Value == 1);
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == OUTCOMES_INSTRUMENT && measurement.Outcome == GetProducerWhitelabelDiagnostics.OUTCOME_SERVED && measurement.Value == 1);
        Assert.Contains(_logger.Logs, log => log.EventId == 3001 && log.Message.Contains(SLUG));
        Assert.NotEmpty(_logger.Scopes);
    }
}
