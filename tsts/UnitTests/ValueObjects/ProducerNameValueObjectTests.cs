using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class ProducerNameValueObjectTests
{
    [Fact]
    public void Given_NameWithExtraSpaces_When_Building_Should_NormalizeSpaces()
    {
        // Act
        var valueObject = ProducerNameValueObject.Build("  Ação   Eventos ");

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
        // Act
        var valueObject = ProducerNameValueObject.Build(name);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PRODUCER_NAME_COULD_NOT_BE_EMPTY", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_NameAtMaxLength_When_Building_Should_BeValid()
    {
        // Act
        var valueObject = ProducerNameValueObject.Build(new string('a', ProducerNameValueObject.MAX_LENGTH));

        // Assert
        Assert.True(valueObject.IsValid);
    }

    [Fact]
    public void Given_NameOverMaxLength_When_Building_Should_ReturnLengthNotification()
    {
        // Act
        var valueObject = ProducerNameValueObject.Build(new string('a', ProducerNameValueObject.MAX_LENGTH + 1));

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PRODUCER_NAME_LENGTH_INVALID", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }
}
