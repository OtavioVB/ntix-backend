using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class EmailValueObjectTests
{
    [Theory]
    [InlineData("maria@ntix.com", "maria@ntix.com")]
    [InlineData("  Maria@NTIX.com ", "maria@ntix.com")]
    public void Given_ValidEmail_When_Building_Should_ReturnNormalizedValue(string email, string expected)
    {
        // Arrange & Act
        var valueObject = EmailValueObject.Build(email);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyEmail_When_Building_Should_ReturnOnlyEmptyNotification(string? email)
    {
        // Arrange & Act
        var valueObject = EmailValueObject.Build(email);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("EMAIL_COULD_NOT_BE_EMPTY", notification.Code);
    }

    [Theory]
    [InlineData("maria")]
    [InlineData("maria@")]
    [InlineData("@ntix.com")]
    [InlineData("Maria <maria@ntix.com>")]
    public void Given_EmailWithInvalidFormat_When_Building_Should_ReturnFormatNotification(string email)
    {
        // Arrange & Act
        var valueObject = EmailValueObject.Build(email);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "EMAIL_FORMAT_INVALID");
    }

    [Fact]
    public void Given_EmailOverMaximumLength_When_Building_Should_ReturnLengthNotification()
    {
        // Arrange
        var email = $"{new string('a', EmailValueObject.MAX_LENGTH)}@ntix.com";

        // Act
        var valueObject = EmailValueObject.Build(email);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "EMAIL_LENGTH_INVALID");
    }

    [Theory]
    [InlineData("maria@ntix.com", "ma***@ntix.com")]
    [InlineData("abc@ntix.com", "ab***@ntix.com")]
    [InlineData("ab@ntix.com", "a***@ntix.com")]
    [InlineData("a@ntix.com", "a***@ntix.com")]
    public void Given_ValidEmail_When_Obfuscating_Should_HideMostOfLocalPartAndItsLength(string email, string expected)
    {
        // Arrange
        var valueObject = EmailValueObject.Build(email);

        // Act
        var obfuscated = valueObject.GetObfuscatedValue();

        // Assert
        Assert.Equal(expected, obfuscated);
    }

    [Fact]
    public void Given_InvalidEmail_When_Obfuscating_Should_ThrowInvalidOperationException()
    {
        // Arrange
        var valueObject = EmailValueObject.Build("invalid");

        // Act
        var obfuscate = () => valueObject.GetObfuscatedValue();

        // Assert
        Assert.Throws<InvalidOperationException>(obfuscate);
    }
}
