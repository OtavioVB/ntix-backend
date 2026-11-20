using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.BoundedContexts.EventContext.Services;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Events;

public sealed class TicketBatchTests
{
    private static readonly IdValueObject CORRELATION_ID = IdValueObject.Build(EventTestData.CORRELATION_ID.ToString());

    private static string SingleCode(Domain.CrossCutting.MethodResultContext.MethodResult result)
        => Assert.Single(result.Notifications).Code;

    [Theory]
    [InlineData(100L, true)]
    [InlineData(10_000_000L, true)]
    [InlineData(99L, false)]
    [InlineData(10_000_001L, false)]
    [InlineData(null, false)]
    public void Given_Price_When_Building_Should_AcceptFromOneRealToOneHundredThousand(long? cents, bool expected)
        => Assert.Equal(expected, TicketPriceValueObject.Build(cents).IsValid);

    [Fact]
    public void Given_PriceInCents_When_ConvertingToAmount_Should_BeExactAndRoundTrip()
    {
        // Arrange
        var price = TicketPriceValueObject.Build(12_345);

        // Act & Assert
        Assert.Equal(123.45m, price.Amount);
        Assert.Equal(12_345, TicketPriceValueObject.FromAmount(price.Amount).Cents);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public void Given_Quantity_When_Building_Should_AcceptFromOneToOneHundredThousand(int quantity, bool expected)
        => Assert.Equal(expected, BatchQuantityValueObject.Build(quantity).IsValid);

    [Theory]
    [InlineData("", "BATCH_NAME_INVALID")]
    [InlineData(null, "BATCH_NAME_INVALID")]
    public void Given_EmptyName_When_Building_Should_ReportNameInvalid(string? name, string code)
        => Assert.Equal(code, SingleCode(BatchNameValueObject.Build(name).MethodResult));

    [Fact]
    public void Given_LongNote_When_Building_Should_ReportNoteInvalid()
        => Assert.Equal("BATCH_NOTE_INVALID", SingleCode(BatchNoteValueObject.Build(new string('a', BatchNoteValueObject.MAX_LENGTH + 1)).MethodResult));

    [Theory]
    [InlineData("2027-01-31T00:00:00Z", "2026-12-01T00:00:00Z")]
    [InlineData("2026-12-01T00:00:00Z", "2026-12-01T00:00:00Z")]
    [InlineData("amanhã", "2026-12-01T00:00:00Z")]
    [InlineData(null, "2026-12-01T00:00:00Z")]
    public void Given_InvalidSalesPeriod_When_Building_Should_ReportSalesPeriodInvalid(string? startsAt, string? endsAt)
        => Assert.Equal("BATCH_SALES_PERIOD_INVALID", SingleCode(SalesPeriodValueObject.Build(startsAt, endsAt).MethodResult));

    [Theory]
    [InlineData("PAUSED")]
    [InlineData(null)]
    public void Given_UnknownStatus_When_Building_Should_ReportStatusInvalid(string? status)
        => Assert.Equal("BATCH_STATUS_INVALID", SingleCode(BatchStatusValueObject.Build(status).MethodResult));

    [Fact]
    public void Given_ValidDetails_When_Creating_Should_BelongToEventAndProducer()
    {
        // Arrange
        var @event = EventTestData.NewEvent();

        // Act
        var result = TicketBatch.Create(@event, TicketBatchTestData.Details(), EventTestData.NOW, CORRELATION_ID);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(@event.Id, result.Output!.EventId);
        Assert.Equal(@event.ProducerId, result.Output.ProducerId);
        Assert.Equal(EventTestData.NOW, result.Output.CreatedAt.Value);
    }

    [Fact]
    public void Given_SalesEndingAfterEvent_When_Creating_Should_ReportSalesPeriodInvalid()
    {
        // Arrange & Act
        var result = TicketBatch.Create(EventTestData.NewEvent(), TicketBatchTestData.Details(salesEndsAt: EventTestData.ENDS_AT.AddMinutes(1)), EventTestData.NOW, CORRELATION_ID);

        // Assert
        Assert.Equal("BATCH_SALES_PERIOD_INVALID", Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_EventAlreadyStarted_When_Creating_Should_ReportAlreadyStarted()
    {
        // Arrange & Act
        var result = TicketBatch.Create(EventTestData.NewEvent(), TicketBatchTestData.Details(), EventTestData.STARTS_AT.AddMinutes(5), CORRELATION_ID);

        // Assert
        Assert.Equal(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(99, false)]
    public void Given_SoldAndReserved_When_UpdatingQuantity_Should_NotGoBelowThem(int newQuantity, bool expected)
    {
        // Arrange
        var batch = TicketBatchTestData.NewBatch();

        // Act
        var result = batch.Update(EventTestData.NewEvent(), TicketBatchTestData.Details(quantity: newQuantity), soldAndReserved: 100, EventTestData.NOW, CORRELATION_ID);

        // Assert
        Assert.Equal(expected, result.IsSuccess);
        if (!expected)
            Assert.Equal(TicketBatch.BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public void Given_Pause_When_Updating_Should_KeepCreationAndTouchUpdate()
    {
        // Arrange
        var batch = TicketBatchTestData.NewBatch();
        var later = EventTestData.NOW.AddHours(1);

        // Act
        var result = batch.Update(EventTestData.NewEvent(), TicketBatchTestData.Details(status: "INACTIVE"), 0, later, CORRELATION_ID);

        // Assert
        Assert.Equal(BatchStatus.INACTIVE, result.Output!.Details.Status.Value);
        Assert.Equal(batch.CreatedAt, result.Output.CreatedAt);
        Assert.Equal(later, result.Output.UpdatedAt.Value);
    }

    [Theory]
    [InlineData("ACTIVE", -1, 0, BatchSaleState.UPCOMING)]
    [InlineData("ACTIVE", 1, 0, BatchSaleState.ON_SALE)]
    [InlineData("ACTIVE", 1, 500, BatchSaleState.SOLD_OUT)]
    [InlineData("ACTIVE", 100, 0, BatchSaleState.CLOSED)]
    [InlineData("INACTIVE", 1, 0, BatchSaleState.PAUSED)]
    public void Given_BatchAndInstant_When_CalculatingAvailability_Should_DeriveSaleState(string status, int daysAfterSalesStart, int sold, BatchSaleState expected)
    {
        // Arrange
        var batch = TicketBatchTestData.NewBatch(TicketBatchTestData.Details(status: status));

        // Act
        var result = TicketBatchAvailability.Calculate(batch, sold, reserved: 0, TicketBatchTestData.SALES_STARTS_AT.AddDays(daysAfterSalesStart));

        // Assert
        Assert.Equal(expected, result.SaleState);
    }

    [Fact]
    public void Given_SoldAndReserved_When_CalculatingAvailability_Should_SubtractBothAndNeverGoNegative()
    {
        // Arrange
        var batch = TicketBatchTestData.NewBatch(TicketBatchTestData.Details(quantity: 10));

        // Act
        var partial = TicketBatchAvailability.Calculate(batch, sold: 3, reserved: 2, TicketBatchTestData.SALES_STARTS_AT);
        var oversold = TicketBatchAvailability.Calculate(batch, sold: 9, reserved: 4, TicketBatchTestData.SALES_STARTS_AT);

        // Assert
        Assert.Equal(5, partial.Available);
        Assert.Equal(0, oversold.Available);
        Assert.Equal(BatchSaleState.SOLD_OUT, oversold.SaleState);
    }
}
