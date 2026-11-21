using Domain.BoundedContexts.EventContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Events;

/// <summary>Situação exposta do evento (REQ14, RN1 e RN2): ENDED a partir do término, publicado ou não.</summary>
public sealed class EventStatusTests
{
    [Fact]
    public void Given_DraftOneSecondBeforeTheEnd_When_GettingStatus_Should_StillBeDraft()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var status = @event.StatusAt(EventTestData.ENDS_AT.AddSeconds(-1));

        // Assert
        Assert.Equal(EventStatus.DRAFT, status);
    }

    [Fact]
    public void Given_PublishedEventAtItsEnd_When_GettingStatus_Should_BeEnded()
    {
        // Arrange
        var @event = EventTestData.NewEvent() with { Status = EventStatus.PUBLISHED };

        // Act
        var status = @event.StatusAt(EventTestData.ENDS_AT);

        // Assert
        Assert.Equal(EventStatus.ENDED, status);
    }

    [Fact]
    public void Given_DraftAfterItsEnd_When_GettingStatus_Should_BeEnded()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var status = @event.StatusAt(EventTestData.ENDS_AT.AddDays(1));

        // Assert
        Assert.Equal(EventStatus.ENDED, status);
    }

    [Fact]
    public void Given_PublishedEventBeforeItsEnd_When_GettingStatus_Should_KeepTheStoredStatus()
    {
        // Arrange
        var @event = EventTestData.NewEvent() with { Status = EventStatus.PUBLISHED };

        // Act
        var status = @event.StatusAt(EventTestData.NOW);

        // Assert
        Assert.Equal(EventStatus.PUBLISHED, status);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("PUBLISHED")]
    [InlineData("ENDED")]
    public void Given_KnownStatus_When_BuildingTheFilter_Should_BeValid(string status)
    {
        // Arrange & Act
        var filter = EventStatusValueObject.Build(status);

        // Assert
        Assert.True(filter.IsValid);
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("CANCELED")]
    public void Given_UnknownStatus_When_BuildingTheFilter_Should_ReportStatusInvalid(string status)
    {
        // Arrange & Act
        var filter = EventStatusValueObject.Build(status);

        // Assert
        Assert.Equal("EVENT_STATUS_INVALID", Assert.Single(filter.MethodResult.Notifications).Code);
    }
}
