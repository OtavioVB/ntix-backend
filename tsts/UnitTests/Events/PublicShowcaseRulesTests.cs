using Application.UseCases.Base.Events;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Enums;
using Domain.BoundedContexts.EventContext.Services;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Events.Models;
using UnitTests.Support;

namespace UnitTests.Events;

public sealed class PublicShowcaseRulesTests
{
    private static TicketBatch Batch(long priceCents)
        => TicketBatchTestData.NewBatch(TicketBatchTestData.Details(priceCents: priceCents));

    public static TheoryData<BatchSaleState[], EventSaleState> SaleStateCombinations => new()
    {
        { [BatchSaleState.CLOSED, BatchSaleState.ON_SALE, BatchSaleState.UPCOMING], EventSaleState.ON_SALE },
        { [BatchSaleState.SOLD_OUT, BatchSaleState.UPCOMING], EventSaleState.UPCOMING },
        { [BatchSaleState.CLOSED, BatchSaleState.SOLD_OUT], EventSaleState.SOLD_OUT },
        { [BatchSaleState.CLOSED], EventSaleState.CLOSED },
        { [BatchSaleState.PAUSED], EventSaleState.CLOSED },
        { [], EventSaleState.CLOSED },
    };

    [Theory]
    [MemberData(nameof(SaleStateCombinations))]
    public void Given_BatchStates_When_Summarizing_Should_FollowThePriority(BatchSaleState[] states, EventSaleState expected)
    {
        // Arrange
        var batches = states.Select(state => (Batch(10000), state)).ToArray();

        // Act & Assert
        Assert.Equal(expected, EventSaleSummary.Summarize(batches).SaleState);
    }

    [Fact]
    public void Given_BatchesOnSaleAndUpcoming_When_Summarizing_Should_TakeLowestPriceOnSaleOnly()
    {
        // Arrange & Act
        var summary = EventSaleSummary.Summarize([
            (Batch(15000), BatchSaleState.ON_SALE),
            (Batch(12000), BatchSaleState.ON_SALE),
            (Batch(5000), BatchSaleState.UPCOMING),
            (Batch(1000), BatchSaleState.PAUSED),
        ]);

        // Assert
        Assert.Equal(12000, summary.PriceFromCents);
    }

    [Fact]
    public void Given_OnlyUpcomingBatches_When_Summarizing_Should_TakeLowestUpcomingPrice()
        => Assert.Equal(5000, EventSaleSummary.Summarize([(Batch(8000), BatchSaleState.UPCOMING), (Batch(5000), BatchSaleState.UPCOMING)]).PriceFromCents);

    [Fact]
    public void Given_SoldOutEvent_When_Summarizing_Should_HaveNoPrice()
        => Assert.Null(EventSaleSummary.Summarize([(Batch(8000), BatchSaleState.SOLD_OUT)]).PriceFromCents);

    [Theory]
    [InlineData(500, 0, 6)]
    [InlineData(500, 498, 2)]
    [InlineData(500, 500, 0)]
    public void Given_Availability_When_ConvertingToPublicBatch_Should_CapMaxSelectableByOrderLimit(int quantity, int sold, int expected)
    {
        // Arrange
        var model = TicketBatchTestData.NewModel(TicketBatchTestData.Details(quantity: quantity));
        var sales = new Dictionary<Guid, TicketBatchSalesModel> { [model.Id] = new(sold, 0) };

        // Act
        var item = Assert.Single(PublicEventConversions.Evaluate([model], sales, TicketBatchTestData.SALES_STARTS_AT.AddDays(1)));
        var output = PublicEventConversions.ToBatch(item, maxTicketsPerOrder: 6);

        // Assert
        Assert.Equal(expected, output.MaxSelectable);
    }

    [Fact]
    public void Given_UpcomingBatch_When_ConvertingToPublicBatch_Should_HaveZeroSelectable()
    {
        // Arrange
        var model = TicketBatchTestData.NewModel();

        // Act
        var item = Assert.Single(PublicEventConversions.Evaluate([model], new Dictionary<Guid, TicketBatchSalesModel>(), EventTestData.NOW));

        // Assert
        Assert.Equal("UPCOMING", PublicEventConversions.ToBatch(item, 6).SaleState);
        Assert.Equal(0, PublicEventConversions.ToBatch(item, 6).MaxSelectable);
    }

    [Fact]
    public void Given_PausedBatch_When_Evaluating_Should_HideIt()
        => Assert.Empty(PublicEventConversions.Evaluate([TicketBatchTestData.NewModel(TicketBatchTestData.Details(status: "INACTIVE"))], new Dictionary<Guid, TicketBatchSalesModel>(), EventTestData.NOW));

    [Fact]
    public void Given_NoFilters_When_BuildingSearchFilter_Should_UseFirstPageOfTwenty()
    {
        // Arrange & Act
        var filter = EventSearchFilterValueObject.Build(null, null, null, null, null, null, null, null);

        // Assert
        Assert.True(filter.IsValid);
        Assert.Equal(1, filter.Page);
        Assert.Equal(20, filter.PageSize);
        Assert.Equal(0, filter.Offset);
    }

    [Fact]
    public void Given_ValidFilters_When_BuildingSearchFilter_Should_NormalizeThem()
    {
        // Arrange & Act
        var filter = EventSearchFilterValueObject.Build("mare-cultural", "FESTIVALS", " São Paulo ", "sp", "2027-01-01", "2027-12-31", 3, 10);

        // Assert
        Assert.Equal("mare-cultural", filter.ProducerSlug);
        Assert.Equal(EventCategory.FESTIVALS, filter.Category);
        Assert.Equal("São Paulo", filter.City);
        Assert.Equal("SP", filter.State);
        Assert.Equal(20, filter.Offset);
        Assert.Equal(DateTimeKind.Utc, filter.StartsFrom!.Value.Kind);
    }

    [Theory]
    [InlineData("../x", null, null, null, null, null, null)]
    [InlineData(null, "RODEIO", null, null, null, null, null)]
    [InlineData(null, null, "XX", null, null, null, null)]
    [InlineData(null, null, null, "ontem", null, null, null)]
    [InlineData(null, null, null, "2027-12-31", "2027-01-01", null, null)]
    [InlineData(null, null, null, null, null, 0, null)]
    [InlineData(null, null, null, null, null, null, 51)]
    public void Given_InvalidFilter_When_Building_Should_ReportFilterInvalid(string? producer, string? category, string? state, string? startsFrom, string? startsTo, int? page, int? pageSize)
    {
        // Arrange & Act
        var filter = EventSearchFilterValueObject.Build(producer, category, null, state, startsFrom, startsTo, page, pageSize);

        // Assert
        Assert.False(filter.IsValid);
        Assert.Equal(EventSearchFilterValueObject.EVENT_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(filter.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_SeveralInvalidFilters_When_Building_Should_ReportEachOne()
        => Assert.Equal(3, EventSearchFilterValueObject.Build(null, "RODEIO", new string('a', 81), "XX", null, null, null, null).MethodResult.Notifications.Length);
}
