using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class WhitelabelValueObjectsTests
{
    [Theory]
    [InlineData("#c99b14")]
    [InlineData("#FFFFFF")]
    public void Given_HexColor_When_BuildingColor_Should_BeValid(string value)
    {
        // Act
        var color = ColorValueObject.Build(value);

        // Assert
        Assert.True(color.IsValid);
        Assert.Equal(value, color.Value);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#c99b14; } body { display: none")]
    [InlineData("rgb(0, 0, 0)")]
    [InlineData("url(https://evil.com/x.png)")]
    public void Given_ColorOutsideHex_When_BuildingColor_Should_ReturnFormatInvalid(string value)
    {
        // Act
        var color = ColorValueObject.Build(value);

        // Assert
        Assert.False(color.IsValid);
        Assert.Contains(color.MethodResult.Notifications, notification => notification.Code == "COLOR_FORMAT_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyColor_When_BuildingColor_Should_ReturnCouldNotBeEmpty(string? value)
    {
        // Act
        var color = ColorValueObject.Build(value);

        // Assert
        Assert.False(color.IsValid);
        Assert.Contains(color.MethodResult.Notifications, notification => notification.Code == "COLOR_COULD_NOT_BE_EMPTY");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4px")]
    [InlineData("0.75rem")]
    [InlineData("90rem")]
    [InlineData("100%")]
    [InlineData("clamp(1.0625rem, 1rem + 0.3vw, 1.25rem)")]
    [InlineData("calc(100% - 2rem)")]
    public void Given_CssLength_When_BuildingLength_Should_BeValid(string value)
    {
        // Act
        var length = CssLengthValueObject.Build(value);

        // Assert
        Assert.True(length.IsValid);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("1rem; color: red")]
    [InlineData("clamp(1rem, url(https://evil.com), 2rem)")]
    [InlineData("expression(alert(1))")]
    [InlineData("var(--n-space-4)")]
    public void Given_InvalidCssLength_When_BuildingLength_Should_ReturnFormatInvalid(string value)
    {
        // Act
        var length = CssLengthValueObject.Build(value);

        // Assert
        Assert.False(length.IsValid);
        Assert.Contains(length.MethodResult.Notifications, notification => notification.Code == "CSS_LENGTH_FORMAT_INVALID");
    }

    [Theory]
    [InlineData("140ms", true)]
    [InlineData("1.2s", true)]
    [InlineData("140", false)]
    [InlineData("fast", false)]
    public void Given_Duration_When_BuildingDuration_Should_ValidateUnit(string value, bool expectedValid)
    {
        // Act
        var duration = CssDurationValueObject.Build(value);

        // Assert
        Assert.Equal(expectedValid, duration.IsValid);
    }

    [Theory]
    [InlineData("cubic-bezier(0.16, 1, 0.3, 1)", true)]
    [InlineData("ease-in-out", true)]
    [InlineData("linear", true)]
    [InlineData("cubic-bezier(0.16, 1, 0.3)", false)]
    [InlineData("steps(4)", false)]
    public void Given_Easing_When_BuildingEasing_Should_AcceptOnlyCubicBezierOrKeyword(string value, bool expectedValid)
    {
        // Act
        var easing = CssEasingValueObject.Build(value);

        // Assert
        Assert.Equal(expectedValid, easing.IsValid);
    }

    [Theory]
    [InlineData("\"Archivo Variable\", \"Segoe UI\", system-ui, -apple-system, sans-serif", true)]
    [InlineData("'IBM Plex Mono', monospace", true)]
    [InlineData("Inter", true)]
    [InlineData("Inter; } body { display: none", false)]
    [InlineData("url(https://evil.com/font.woff)", false)]
    [InlineData("\"Inter\\\"", false)]
    [InlineData("<script>", false)]
    public void Given_FontFamily_When_BuildingFontFamily_Should_RejectWhatOpensRulesOrLoadsResources(string value, bool expectedValid)
    {
        // Act
        var fontFamily = FontFamilyValueObject.Build(value);

        // Assert
        Assert.Equal(expectedValid, fontFamily.IsValid);
    }

    [Theory]
    [InlineData("https://cdn.ntix.com.br/logo.svg", true)]
    [InlineData("/favicon.svg", true)]
    [InlineData("http://cdn.ntix.com.br/logo.svg", false)]
    [InlineData("//evil.com/logo.svg", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,AAAA", false)]
    [InlineData("/logo .svg", false)]
    [InlineData("/\\evil.com", false)]
    public void Given_AssetUrl_When_BuildingAssetUrl_Should_AcceptOnlyHttpsOrOwnPath(string value, bool expectedValid)
    {
        // Act
        var assetUrl = AssetUrlValueObject.Build(value);

        // Assert
        Assert.Equal(expectedValid, assetUrl.IsValid);

        if (!expectedValid)
            Assert.Contains(assetUrl.MethodResult.Notifications, notification => notification.Code == "ASSET_URL_FORMAT_INVALID");
    }

    [Fact]
    public void Given_BrandNameAboveMaxLength_When_BuildingBrandName_Should_ReturnFormatInvalid()
    {
        // Arrange
        var name = new string('a', BrandNameValueObject.MAX_LENGTH + 1);

        // Act
        var brandName = BrandNameValueObject.Build(name);

        // Assert
        Assert.False(brandName.IsValid);
        Assert.Contains(brandName.MethodResult.Notifications, notification => notification.Code == "BRAND_NAME_FORMAT_INVALID");
    }

    [Fact]
    public void Given_BrandNameWithControlCharacter_When_BuildingBrandName_Should_ReturnFormatInvalid()
    {
        // Act
        var brandName = BrandNameValueObject.Build("Palco\u0000Sul");

        // Assert
        Assert.False(brandName.IsValid);
    }

    [Fact]
    public void Given_BrandNameWithSpaces_When_BuildingBrandName_Should_Trim()
    {
        // Act
        var brandName = BrandNameValueObject.Build("  Palco Sul  ");

        // Assert
        Assert.True(brandName.IsValid);
        Assert.Equal("Palco Sul", brandName.Value);
    }
}
