using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class FullNameValueObjectTests
{
    [Theory]
    [InlineData("Maria da Silva", "Maria da Silva")]
    [InlineData("  Maria   da  Silva ", "Maria da Silva")]
    public void Given_ValidFullName_When_Building_Should_ReturnNormalizedValue(string fullName, string expected)
    {
        // Arrange & Act
        var valueObject = FullNameValueObject.Build(fullName);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.True(valueObject.MethodResult.IsSuccess);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyFullName_When_Building_Should_ReturnEmptyNotification(string? fullName)
    {
        // Arrange & Act
        var valueObject = FullNameValueObject.Build(fullName);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "FULL_NAME_COULD_NOT_BE_EMPTY");
    }

    [Fact]
    public void Given_FullNameWithMaximumLength_When_Building_Should_BeValid()
    {
        // Arrange
        var fullName = new string('a', FullNameValueObject.MAX_LENGTH);

        // Act
        var valueObject = FullNameValueObject.Build(fullName);

        // Assert
        Assert.True(valueObject.IsValid);
    }

    [Fact]
    public void Given_FullNameOverMaximumLength_When_Building_Should_ReturnLengthNotification()
    {
        // Arrange
        var fullName = new string('a', FullNameValueObject.MAX_LENGTH + 1);

        // Act
        var valueObject = FullNameValueObject.Build(fullName);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "FULL_NAME_LENGTH_INVALID");
    }

    [Fact]
    public void Given_InvalidFullName_When_GettingValue_Should_ThrowInvalidOperationException()
    {
        // Arrange
        var valueObject = FullNameValueObject.Build(string.Empty);

        // Act
        var getValue = () => valueObject.Value;

        // Assert
        Assert.Throws<InvalidOperationException>(getValue);
    }
}
