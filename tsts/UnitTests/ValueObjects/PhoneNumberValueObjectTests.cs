using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class PhoneNumberValueObjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_NoPhoneNumber_When_Building_Should_BeValidWithoutValue(string? phoneNumber)
    {
        // Arrange & Act
        var valueObject = PhoneNumberValueObject.Build(phoneNumber);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Null(valueObject.Value);
    }

    [Theory]
    [InlineData("11987654321")]
    [InlineData("(11) 98765-4321")]
    [InlineData(" 11 98765 4321 ")]
    public void Given_PhoneNumberWithAreaCode_When_Building_Should_KeepOnlyDigits(string phoneNumber)
    {
        // Arrange & Act
        var valueObject = PhoneNumberValueObject.Build(phoneNumber);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal("11987654321", valueObject.Value);
    }

    [Theory]
    [InlineData("987654321")]
    [InlineData("1198765432")]
    [InlineData("119876543210")]
    [InlineData("+5511987654321")]
    [InlineData("1198765432a")]
    public void Given_PhoneNumberWithoutAreaCodeAndNumber_When_Building_Should_ReturnFormatNotification(string phoneNumber)
    {
        // Arrange & Act
        var valueObject = PhoneNumberValueObject.Build(phoneNumber);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("PHONE_NUMBER_FORMAT_INVALID", notification.Code);
    }
}
