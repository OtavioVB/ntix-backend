using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class ProducerNameValueObjectTests
{
    [Fact]
    public void Given_NameWithExtraSpaces_When_Building_Should_NormalizeSpaces()
    {
        // Arrange & Act
        var valueObject = ProducerNameValueObject.Build("  Ação   Eventos  ");

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal("Ação Eventos", valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyName_When_Building_Should_ReturnEmptyNotification(string? name)
    {
        // Arrange & Act
        var valueObject = ProducerNameValueObject.Build(name);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PRODUCER_NAME_COULD_NOT_BE_EMPTY", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_NameAboveMaxLength_When_Building_Should_ReturnLengthNotification()
    {
        // Arrange
        var name = new string('a', ProducerNameValueObject.MAX_LENGTH + 1);

        // Act
        var valueObject = ProducerNameValueObject.Build(name);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PRODUCER_NAME_LENGTH_INVALID", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_NameAtMaxLength_When_Building_Should_BeValid()
    {
        // Arrange & Act
        var valueObject = ProducerNameValueObject.Build(new string('a', ProducerNameValueObject.MAX_LENGTH));

        // Assert
        Assert.True(valueObject.IsValid);
    }
}
