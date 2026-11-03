using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class PhoneValueObjectTests
{
    [Theory]
    [InlineData("(11) 98765-4321", "+5511987654321")]
    [InlineData("11987654321", "+5511987654321")]
    [InlineData("(11) 3456-7890", "+551134567890")]
    [InlineData("+55 11 98765-4321", "+5511987654321")]
    [InlineData("+5511999999999", "+5511999999999")]
    [InlineData("+14155552671", "+14155552671")]
    public void Given_ValidPhone_When_Building_Should_NormalizeToE164(string phone, string expected)
    {
        // Arrange & Act
        var valueObject = PhoneValueObject.Build(phone);

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
        // Arrange & Act
        var valueObject = PhoneValueObject.Build(phone);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PHONE_COULD_NOT_BE_EMPTY", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }

    [Theory]
    [InlineData("987654321")]
    [InlineData("119876543210")]
    [InlineData("(11) 9876a-4321")]
    [InlineData("+")]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    [InlineData("+0511987654321")]
    public void Given_InvalidPhone_When_Building_Should_ReturnFormatNotification(string phone)
    {
        // Arrange & Act
        var valueObject = PhoneValueObject.Build(phone);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PHONE_FORMAT_INVALID", Assert.Single(valueObject.MethodResult.Notifications).Code);
        Assert.Throws<InvalidOperationException>(() => valueObject.Value);
    }
}
