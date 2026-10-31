using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class PasswordValueObjectTests
{
    [Theory]
    [InlineData("Senha@12")]
    [InlineData("Senha com espaço 1!")]
    [InlineData("  Senha@123  ")]
    public void Given_ValidPassword_When_Building_Should_KeepValueUnchanged(string password)
    {
        // Arrange & Act
        var valueObject = PasswordValueObject.Build(password);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(password, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Given_EmptyPassword_When_Building_Should_ReturnOnlyEmptyNotification(string? password)
    {
        // Arrange & Act
        var valueObject = PasswordValueObject.Build(password);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("PASSWORD_COULD_NOT_BE_EMPTY", notification.Code);
    }

    [Theory]
    [InlineData("Senha@1", "PASSWORD_TOO_SHORT")]
    [InlineData("senha@123", "PASSWORD_WITHOUT_UPPERCASE")]
    [InlineData("SENHA@123", "PASSWORD_WITHOUT_LOWERCASE")]
    [InlineData("Senha@abc", "PASSWORD_WITHOUT_NUMBER")]
    [InlineData("Senha1234", "PASSWORD_WITHOUT_SYMBOL")]
    [InlineData("Senha 1234", "PASSWORD_WITHOUT_SYMBOL")]
    public void Given_PasswordBreakingOneRule_When_Building_Should_ReturnNotificationOfThatRule(string password, string expectedCode)
    {
        // Arrange & Act
        var valueObject = PasswordValueObject.Build(password);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal(expectedCode, notification.Code);
    }

    [Fact]
    public void Given_PasswordBreakingAllRules_When_Building_Should_ReturnOneNotificationPerRule()
    {
        // Arrange & Act
        var valueObject = PasswordValueObject.Build(" ");

        // Assert
        Assert.Collection(
            valueObject.MethodResult.Notifications,
            notification => Assert.Equal("PASSWORD_TOO_SHORT", notification.Code),
            notification => Assert.Equal("PASSWORD_WITHOUT_UPPERCASE", notification.Code),
            notification => Assert.Equal("PASSWORD_WITHOUT_LOWERCASE", notification.Code),
            notification => Assert.Equal("PASSWORD_WITHOUT_NUMBER", notification.Code),
            notification => Assert.Equal("PASSWORD_WITHOUT_SYMBOL", notification.Code));
    }

    [Fact]
    public void Given_InvalidPassword_When_ReadingValue_Should_Throw()
    {
        // Arrange
        var valueObject = PasswordValueObject.Build("invalida");

        // Act
        var read = () => valueObject.Value;

        // Assert
        Assert.Throws<InvalidOperationException>(read);
    }
}
