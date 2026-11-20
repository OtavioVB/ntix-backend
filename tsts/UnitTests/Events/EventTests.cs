using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Events;

public sealed class EventTests
{
    private static readonly IdValueObject PRODUCER_ID = IdValueObject.Build(EventTestData.PRODUCER_ID.ToString());
    private static readonly IdValueObject CORRELATION_ID = IdValueObject.Build(EventTestData.CORRELATION_ID.ToString());

    private static Domain.CrossCutting.MethodResultContext.MethodResult<Event> Create(EventDetails details, DateTime now)
        => Event.Create(PRODUCER_ID, SlugValueObject.Build(EventTestData.SLUG, Event.SLUG_MAX_LENGTH), details, now, CORRELATION_ID);

    [Fact]
    public void Given_ValidDetails_When_Creating_Should_BeDraftWithAuditFields()
    {
        // Arrange & Act
        var result = Create(EventTestData.Details(), EventTestData.NOW);

        // Assert
        Assert.True(result.IsSuccess);
        var @event = result.Output!;
        Assert.Equal(EventStatus.DRAFT, @event.Status);
        Assert.Equal(EventTestData.SLUG, @event.Slug.Value);
        Assert.Equal(EventTestData.NOW, @event.CreatedAt.Value);
        Assert.Equal(EventTestData.NOW, @event.UpdatedAt.Value);
        Assert.Equal(EventTestData.CORRELATION_ID, @event.CreatedCorrelationId.Value);
    }

    [Fact]
    public void Given_StartInThePast_When_Creating_Should_ReportPeriodInvalid()
    {
        // Arrange & Act
        var result = Create(EventTestData.Details(), EventTestData.STARTS_AT.AddMinutes(1));

        // Assert
        Assert.Equal("EVENT_PERIOD_INVALID", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_InvalidDetails_When_Creating_Should_ReportEveryInvalidField()
    {
        // Arrange & Act
        var result = Create(EventTestData.Details(title: "A", category: "RODEIO", state: "XX"), EventTestData.NOW);

        // Assert
        Assert.Equal(
            ["EVENT_TITLE_INVALID", "EVENT_CATEGORY_INVALID", "EVENT_STATE_INVALID"],
            result.Notifications.Select(notification => notification.Code));
    }

    [Fact]
    public void Given_LongestTitle_When_BuildingBaseSlug_Should_LeaveRoomForRandomVersion()
    {
        // Arrange
        var title = EventTitleValueObject.Build(new string('a', EventTitleValueObject.MAX_LENGTH));

        // Act
        var version = Event.BuildBaseSlug(title).BuildRandomVersion();

        // Assert
        Assert.True(version.Value.Length <= Event.SLUG_MAX_LENGTH);
    }

    [Fact]
    public void Given_EventNotStarted_When_Updating_Should_ReplaceDetailsAndKeepSlug()
    {
        // Arrange
        var @event = EventTestData.NewEvent();
        var newCorrelation = IdValueObject.Build(Guid.NewGuid().ToString());

        // Act
        var result = @event.Update(EventTestData.Details(title: "Festival Horizonte 2027"), EventTestData.NOW, newCorrelation);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Festival Horizonte 2027", result.Output!.Details.Title.Value);
        Assert.Equal(EventTestData.SLUG, result.Output.Slug.Value);
        Assert.Equal(@event.CreatedAt, result.Output.CreatedAt);
        Assert.Equal(newCorrelation.Value, result.Output.UpdatedCorrelationId.Value);
        Assert.Equal(EventTestData.NOW, result.Output.UpdatedAt.Value);
    }

    [Fact]
    public void Given_EventAlreadyStarted_When_Updating_Should_ReportAlreadyStarted()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var result = @event.Update(EventTestData.Details(), EventTestData.STARTS_AT.AddHours(1), CORRELATION_ID);

        // Assert
        Assert.Equal(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_NewStartInThePast_When_Updating_Should_ReportPeriodInvalid()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var result = @event.Update(
            EventTestData.Details(startsAt: EventTestData.NOW.AddDays(-1), endsAt: EventTestData.NOW.AddHours(-12), doorsOpenAt: string.Empty),
            EventTestData.NOW,
            CORRELATION_ID);

        // Assert
        Assert.Equal("EVENT_PERIOD_INVALID", Assert.Single(result.Notifications).Code);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Given_Instant_When_CheckingIfCanBeEdited_Should_AllowOnlyBeforeStart(int minutesFromStart, bool expected)
        => Assert.Equal(expected, EventTestData.NewEvent().CanBeEdited(EventTestData.STARTS_AT.AddMinutes(minutesFromStart)));

    [Fact]
    public void Given_NewDetails_When_ListingChangedFields_Should_NameOnlyWhatChanged()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var changed = @event.GetChangedFields(EventTestData.Details(title: "Outro título", state: "SP"));

        // Assert
        Assert.Equal(["title", "venue"], changed);
    }
}
