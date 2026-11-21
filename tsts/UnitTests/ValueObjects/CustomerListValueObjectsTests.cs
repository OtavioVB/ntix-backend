using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.ValueObjects;

/// <summary>Filtros e paginação das listas do comprador (REQ13, RN2, RN3, RN5).</summary>
public sealed class CustomerListValueObjectsTests
{
    [Fact]
    public void Given_NoOrderStatus_When_BuildingOrderFilter_Should_ReturnAllOrders()
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildOrderFilter(null);

        // Assert
        Assert.True(filter.IsValid);
        Assert.Null(filter.OrderStatus);
    }

    [Theory]
    [InlineData("PAID", OrderStatus.PAID)]
    [InlineData("paid", OrderStatus.PAID)]
    [InlineData(" EXPIRED ", OrderStatus.EXPIRED)]
    [InlineData("PENDING_PAYMENT", OrderStatus.PENDING_PAYMENT)]
    public void Given_KnownOrderStatus_When_BuildingOrderFilter_Should_AcceptIt(string status, OrderStatus expected)
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildOrderFilter(status);

        // Assert
        Assert.True(filter.IsValid);
        Assert.Equal(expected, filter.OrderStatus);
    }

    [Fact]
    public void Given_UnknownOrderStatus_When_BuildingOrderFilter_Should_RefuseWithOrderFilterCode()
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildOrderFilter("CANCELED");

        // Assert
        Assert.False(filter.IsValid);
        Assert.Equal(CustomerListFiltersValueObject.ORDER_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(filter.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_NoWhen_When_BuildingTicketFilter_Should_DefaultToUpcoming()
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildTicketFilter(null);

        // Assert
        Assert.True(filter.IsValid);
        Assert.Equal(TicketPeriod.UPCOMING, filter.TicketPeriod);
    }

    [Fact]
    public void Given_PastWhen_When_BuildingTicketFilter_Should_SelectPast()
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildTicketFilter("past");

        // Assert
        Assert.True(filter.IsValid);
        Assert.Equal(TicketPeriod.PAST, filter.TicketPeriod);
    }

    [Fact]
    public void Given_UnknownWhen_When_BuildingTicketFilter_Should_RefuseWithTicketFilterCode()
    {
        // Act
        var filter = CustomerListFiltersValueObject.BuildTicketFilter("FUTURE");

        // Assert
        Assert.False(filter.IsValid);
        Assert.Equal(CustomerListFiltersValueObject.TICKET_FILTER_INVALID_NOTIFICATION_CODE, Assert.Single(filter.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_NoPage_When_BuildingPage_Should_DefaultToFirstPageOfTwenty()
    {
        // Act
        var page = CustomerListPageValueObject.Build(null, null, "ORDER_FILTER_INVALID");

        // Assert
        Assert.True(page.IsValid);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(0, page.Offset);
    }

    [Fact]
    public void Given_ThirdPageOfTen_When_BuildingPage_Should_OffsetTwenty()
    {
        // Act
        var page = CustomerListPageValueObject.Build(3, 10, "ORDER_FILTER_INVALID");

        // Assert
        Assert.Equal(20, page.Offset);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    [InlineData(-1, 10)]
    public void Given_PageOutOfRange_When_BuildingPage_Should_RefuseWithTheListCode(int page, int pageSize)
    {
        // Act
        var result = CustomerListPageValueObject.Build(page, pageSize, "ORDER_FILTER_INVALID");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("ORDER_FILTER_INVALID", Assert.Single(result.MethodResult.Notifications).Code);
    }

    [Fact]
    public void Given_PageSizeAtTheLimit_When_BuildingPage_Should_AcceptFifty()
    {
        // Act
        var page = CustomerListPageValueObject.Build(1, 50, "ORDER_FILTER_INVALID");

        // Assert
        Assert.True(page.IsValid);
        Assert.Equal(50, page.PageSize);
    }

    [Fact]
    public void Given_OrderStoredAsPendingWithReservationPassed_When_ComputingEffectiveStatus_Should_BeExpired()
    {
        // Arrange (RN3)
        var expiresAt = OrderTestData.NOW.AddMinutes(15);

        // Act
        var status = Order.EffectiveStatusOf(OrderStatus.PENDING_PAYMENT, expiresAt, expiresAt.AddSeconds(1));

        // Assert
        Assert.Equal(OrderStatus.EXPIRED, status);
    }

    [Fact]
    public void Given_PaidOrderPastItsReservation_When_ComputingEffectiveStatus_Should_StayPaid()
    {
        // Act
        var status = Order.EffectiveStatusOf(OrderStatus.PAID, OrderTestData.NOW.AddMinutes(-5), OrderTestData.NOW);

        // Assert
        Assert.Equal(OrderStatus.PAID, status);
    }

    [Fact]
    public void Given_PendingOrderWithinReservation_When_ComputingEffectiveStatus_Should_StayPending()
    {
        // Act
        var status = Order.EffectiveStatusOf(OrderStatus.PENDING_PAYMENT, OrderTestData.NOW.AddMinutes(5), OrderTestData.NOW);

        // Assert
        Assert.Equal(OrderStatus.PENDING_PAYMENT, status);
    }
}
