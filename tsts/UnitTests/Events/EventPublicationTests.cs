using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Events;

public sealed class EventPublicationTests
{
    private static readonly IdValueObject CORRELATION_ID = IdValueObject.Build(Guid.NewGuid().ToString());

    [Fact]
    public void Given_DraftWithActiveBatchAndPaymentMethod_When_Publishing_Should_BePublished()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var result = @event.Publish([TicketBatchTestData.NewBatch()], hasPaymentMethod: true, EventTestData.NOW, CORRELATION_ID);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EventStatus.PUBLISHED, result.Output!.Status);
        Assert.Equal(EventTestData.NOW, result.Output.UpdatedAt.Value);
        Assert.Equal(@event.Slug.Value, result.Output.Slug.Value);
    }

    [Fact]
    public void Given_PublishedEvent_When_Publishing_Should_ReportNotDraft()
    {
        // Arrange
        var published = EventTestData.NewEvent().Publish([TicketBatchTestData.NewBatch()], true, EventTestData.NOW, CORRELATION_ID).Output!;

        // Act
        var result = published.Publish([TicketBatchTestData.NewBatch()], true, EventTestData.NOW, CORRELATION_ID);

        // Assert
        Assert.Equal(Event.EVENT_NOT_DRAFT_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_EventAlreadyStarted_When_Publishing_Should_ReportAlreadyStarted()
    {
        // Arrange & Act
        var result = EventTestData.NewEvent().Publish([TicketBatchTestData.NewBatch()], true, EventTestData.STARTS_AT.AddMinutes(1), CORRELATION_ID);

        // Assert
        Assert.Equal(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    public static TheoryData<string, DateTime?> BatchesThatDoNotCount => new()
    {
        { "INACTIVE", null },
        { "ACTIVE", TicketBatchTestData.SALES_STARTS_AT.AddDays(1) },
    };

    [Theory]
    [MemberData(nameof(BatchesThatDoNotCount))]
    public void Given_NoActiveBatchWithOpenSales_When_Publishing_Should_ReportWithoutBatch(string status, DateTime? salesEndsAt)
    {
        // Arrange
        var batch = TicketBatchTestData.NewBatch(TicketBatchTestData.Details(status: status, salesEndsAt: salesEndsAt));
        var now = TicketBatchTestData.SALES_STARTS_AT.AddDays(2);

        // Act
        var result = EventTestData.NewEvent().Publish([batch], true, now, CORRELATION_ID);

        // Assert
        Assert.Equal(Event.EVENT_WITHOUT_BATCH_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_NoBatches_When_Publishing_Should_ReportWithoutBatch()
        => Assert.Equal(Event.EVENT_WITHOUT_BATCH_NOTIFICATION_CODE, Assert.Single(EventTestData.NewEvent().Publish([], true, EventTestData.NOW, CORRELATION_ID).Notifications).Code);

    [Fact]
    public void Given_NoPaymentMethod_When_Publishing_Should_ReportWithoutPaymentMethod()
        => Assert.Equal(Event.EVENT_WITHOUT_PAYMENT_METHOD_NOTIFICATION_CODE, Assert.Single(EventTestData.NewEvent().Publish([TicketBatchTestData.NewBatch()], false, EventTestData.NOW, CORRELATION_ID).Notifications).Code);
}
