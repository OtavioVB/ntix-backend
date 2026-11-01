using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class E164PhoneValueObjectTests
{
    [Theory]
    [InlineData("+5511999999999", "+5511999999999")]
    [InlineData("+55 (11) 99999-9999", "+5511999999999")]
    [InlineData(" +1 202 555 0100 ", "+12025550100")]
    public void Given_ValidPhone_When_Building_Should_NormalizeToE164(string phone, string expected)
    {
        // Act
        var valueObject = E164PhoneValueObject.Build(phone);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyPhone_When_Building_Should_ReturnEmptyNotification(string? phone)
    {
        // Act
        var valueObject = E164PhoneValueObject.Build(phone);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PHONE_COULD_NOT_BE_EMPTY", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }

    [Theory]
    [InlineData("11999999999")]
    [InlineData("+0511999999999")]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    [InlineData("+55abc99999999")]
    [InlineData("+")]
    public void Given_InvalidPhone_When_Building_Should_ReturnFormatNotification(string phone)
    {
        // Act
        var valueObject = E164PhoneValueObject.Build(phone);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PHONE_FORMAT_INVALID", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }
}
