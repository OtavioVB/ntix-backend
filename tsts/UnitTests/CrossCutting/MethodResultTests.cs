using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace UnitTests.CrossCutting;

public sealed class MethodResultTests
{
    [Fact]
    public void Given_OnlySuccessResults_When_Aggregating_Should_ReturnSuccess()
    {
        // Arrange
        var first = MethodResult.Success();
        var second = MethodResult.Success();

        // Act
        var result = MethodResult.Factory(first, second);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.Empty(result.Notifications);
    }

    [Fact]
    public void Given_SuccessAndErrorResults_When_Aggregating_Should_ReturnErrorWithAllNotifications()
    {
        // Arrange
        var success = MethodResult.Success([Notification.Success("SUCCESS_CODE", "Sucesso.")]);
        var error = MethodResult.Error([Notification.Error("ERROR_CODE", "Erro.")]);

        // Act
        var result = MethodResult.Factory(success, error);

        // Assert
        Assert.True(result.IsError);
        Assert.Collection(
            result.Notifications,
            notification => Assert.Equal("SUCCESS_CODE", notification.Code),
            notification => Assert.Equal("ERROR_CODE", notification.Code));
    }

    [Fact]
    public void Given_ResultNotInitialized_When_Aggregating_Should_ReturnErrorWithoutThrowing()
    {
        // Arrange
        var notInitialized = default(MethodResult);

        // Act
        var result = MethodResult.Factory(notInitialized);

        // Assert
        Assert.True(result.IsError);
        Assert.Empty(result.Notifications);
    }
}
