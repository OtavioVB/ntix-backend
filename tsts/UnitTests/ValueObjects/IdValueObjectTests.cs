using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class IdValueObjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Given_NoId_When_Building_Should_GenerateId(string? id)
    {
        // Arrange & Act
        var valueObject = IdValueObject.Build(id);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.NotEqual(Guid.Empty, valueObject.Value);
    }

    [Fact]
    public void Given_ValidId_When_Building_Should_KeepId()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var valueObject = IdValueObject.Build(id.ToString());

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(id, valueObject.Value);
    }

    [Fact]
    public void Given_InvalidId_When_Building_Should_ReturnInvalidIdNotification()
    {
        // Arrange & Act
        var valueObject = IdValueObject.Build("not-a-guid");

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("INVALID_ID", notification.Code);
    }
}
