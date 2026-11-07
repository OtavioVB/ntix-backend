using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class SlugValueObjectParseTests
{
    [Theory]
    [InlineData("ntix")]
    [InlineData("acao-eventos-shows")]
    [InlineData("palco-sul-a1b2c")]
    public void Given_CanonicalSlug_When_Parsing_Should_KeepValue(string slug)
    {
        // Act
        var valueObject = SlugValueObject.Parse(slug);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(slug, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../appsettings")]
    [InlineData("..")]
    [InlineData("ntix/../../etc/passwd")]
    [InlineData("ntix.json")]
    [InlineData("Ntix")]
    [InlineData("ntix ")]
    [InlineData("-ntix")]
    [InlineData("ntix--sul")]
    [InlineData("ação")]
    [InlineData("C:\\windows")]
    public void Given_NonCanonicalSlug_When_Parsing_Should_ReturnSlugInvalid(string? slug)
    {
        // Act
        var valueObject = SlugValueObject.Parse(slug);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "SLUG_INVALID");
    }

    [Fact]
    public void Given_SlugAboveSubdomainLimit_When_Parsing_Should_ReturnSlugInvalid()
    {
        // Arrange
        var slug = new string('a', SlugValueObject.SUBDOMAIN_MAX_LENGTH + 1);

        // Act
        var valueObject = SlugValueObject.Parse(slug);

        // Assert
        Assert.False(valueObject.IsValid);
    }

    [Fact]
    public void Given_SlugAtSubdomainLimit_When_Parsing_Should_BeValid()
    {
        // Arrange
        var slug = new string('a', SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Act
        var valueObject = SlugValueObject.Parse(slug);

        // Assert
        Assert.True(valueObject.IsValid);
    }
}
