using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using UnitTests.Support;

namespace UnitTests.Events;

/// <summary>Regras do pedido (REQ6, RN3 a RN9 e RN12) sobre o domínio, sem repositórios.</summary>
public sealed class OrderTests
{
    private static string SingleCode(Domain.CrossCutting.MethodResultContext.MethodResult<Order> result)
        => Assert.Single(result.Notifications).Code;

    [Fact]
    public void Given_ValidSelection_When_Creating_Should_ReserveWithFrozenPriceAndTotal()
    {
        // Arrange & Act
        var result = OrderTestData.Create([OrderTestData.Request(2)]);

        // Assert
        Assert.True(result.IsSuccess);

        var order = result.Output!;

        Assert.Equal(OrderStatus.PENDING_PAYMENT, order.Status);
        Assert.Equal(24000, order.TotalCents);
        Assert.Equal(2, order.TicketCount);
        Assert.Equal(OrderTestData.NOW.AddMinutes(15), order.ExpiresAt.Value);
        Assert.Equal(OrderTestData.NOW, order.CreatedAt.Value);
        Assert.Equal("NX7K2QH9MP", order.Code.Value);
        Assert.Equal(EventTestData.PRODUCER_ID, order.ProducerId.Value);
        Assert.Equal(OrderTestData.CUSTOMER_ID, order.CustomerId.Value);
        Assert.Equal(SalesChannel.HUB, order.SalesChannel);

        var item = Assert.Single(order.Items);

        Assert.Equal("1º lote - Pista", item.BatchName.Value);
        Assert.Equal(12000, item.UnitPrice.Cents);
        Assert.Equal(24000, item.TotalCents);
    }

    [Fact]
    public void Given_TwoBatches_When_Creating_Should_SumTheItemsInTheRequestedOrder()
    {
        // Arrange
        var requested = new[] { OrderTestData.Request(2), OrderTestData.Request(1, OrderTestData.VIP_BATCH_ID) };

        // Act
        var result = OrderTestData.Create(requested, [OrderTestData.Offer(), OrderTestData.VipOffer()]);

        // Assert
        var order = result.Output!;

        Assert.Equal(39000, order.TotalCents);
        Assert.Equal(["1º lote - Pista", "VIP"], order.Items.Select(item => item.BatchName.Value));
    }

    [Fact]
    public void Given_ExactlyTheLimit_When_Creating_Should_Accept()
        => Assert.True(OrderTestData.Create([OrderTestData.Request(6)]).IsSuccess);

    [Fact]
    public void Given_MoreThanTheLimitAcrossBatches_When_Creating_Should_RefuseTheWholeOrder()
    {
        // Arrange
        var requested = new[] { OrderTestData.Request(4), OrderTestData.Request(3, OrderTestData.VIP_BATCH_ID) };

        // Act
        var result = OrderTestData.Create(requested, [OrderTestData.Offer(), OrderTestData.VipOffer()]);

        // Assert
        Assert.Equal(Order.ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_BatchOfAnotherEvent_When_Creating_Should_ReportBatchNotFound()
    {
        // Arrange: o lote pedido não veio entre os lotes do evento.
        var result = OrderTestData.Create([OrderTestData.Request(1, Guid.NewGuid())]);

        // Assert
        Assert.Equal(Order.BATCH_NOT_FOUND_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_PausedBatch_When_Creating_Should_ReportNotOnSale()
    {
        // Arrange
        var offer = OrderTestData.Offer(TicketBatchTestData.Details(status: "INACTIVE"));

        // Act
        var result = OrderTestData.Create([OrderTestData.Request(1)], [offer]);

        // Assert
        Assert.Equal(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_BatchThatOpensTomorrow_When_Creating_Should_ReportNotOnSale()
    {
        // Arrange
        var offer = OrderTestData.Offer(TicketBatchTestData.Details(name: "2º lote", salesStartsAt: OrderTestData.NOW.AddDays(1)));

        // Act
        var result = OrderTestData.Create([OrderTestData.Request(1)], [offer]);

        // Assert
        var notification = Assert.Single(result.Notifications);

        Assert.Equal(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, notification.Code);
        Assert.Contains("2º lote", notification.Message);
    }

    [Fact]
    public void Given_BatchWithClosedSales_When_Creating_Should_ReportNotOnSale()
    {
        // Arrange
        var offer = OrderTestData.Offer(TicketBatchTestData.Details(salesStartsAt: OrderTestData.NOW.AddDays(-10), salesEndsAt: OrderTestData.NOW.AddDays(-1)));

        // Act
        var result = OrderTestData.Create([OrderTestData.Request(1)], [offer]);

        // Assert
        Assert.Equal(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_FewerTicketsThanRequested_When_Creating_Should_ReportAvailabilityWithBatchNameAndRemaining()
    {
        // Arrange: restam 2 (500 - 497 vendidos - 1 reservado) e o comprador pede 3.
        var offer = OrderTestData.Offer(sold: 497, reserved: 1);

        // Act
        var result = OrderTestData.Create([OrderTestData.Request(3)], [offer]);

        // Assert
        var notification = Assert.Single(result.Notifications);

        Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, notification.Code);
        Assert.Contains("1º lote - Pista", notification.Message);
        Assert.Contains("2 ingresso(s)", notification.Message);
    }

    [Fact]
    public void Given_SoldOutBatch_When_Creating_Should_ReportInsufficientAvailabilityAndNotOffSale()
    {
        // Arrange
        var offer = OrderTestData.Offer(sold: 500);

        // Act
        var result = OrderTestData.Create([OrderTestData.Request(1)], [offer]);

        // Assert
        Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_OneBatchWithoutStock_When_Creating_Should_ReserveNothingOfTheOtherItem()
    {
        // Arrange
        var requested = new[] { OrderTestData.Request(2), OrderTestData.Request(2, OrderTestData.VIP_BATCH_ID) };

        // Act
        var result = OrderTestData.Create(requested, [OrderTestData.Offer(), OrderTestData.VipOffer(sold: 49)]);

        // Assert
        Assert.True(result.IsError);
        Assert.Null(result.Output);
        Assert.Equal(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, SingleCode(result));
    }

    [Fact]
    public void Given_SeveralBatchesWithoutStock_When_Creating_Should_ReportEachOne()
    {
        // Arrange
        var requested = new[] { OrderTestData.Request(2), OrderTestData.Request(2, OrderTestData.VIP_BATCH_ID) };

        // Act
        var result = OrderTestData.Create(requested, [OrderTestData.Offer(sold: 499), OrderTestData.VipOffer(sold: 49)]);

        // Assert
        Assert.Equal(2, result.Notifications.Length);
    }

    [Theory]
    [InlineData(-1, OrderStatus.PENDING_PAYMENT)]
    [InlineData(0, OrderStatus.EXPIRED)]
    [InlineData(1, OrderStatus.EXPIRED)]
    public void Given_PendingOrder_When_GettingEffectiveStatus_Should_ExpireAtTheEndOfTheReservation(int minutesAfterExpiry, OrderStatus expected)
    {
        // Arrange
        var order = OrderTestData.NewOrder();

        // Act
        var status = order.GetEffectiveStatus(order.ExpiresAt.Value.AddMinutes(minutesAfterExpiry));

        // Assert
        Assert.Equal(expected, status);
    }

    [Fact]
    public void Given_PaidOrder_When_GettingEffectiveStatus_Should_StayPaidAfterTheReservation()
    {
        // Arrange
        var order = OrderTestData.NewOrder() with { Status = OrderStatus.PAID };

        // Act & Assert
        Assert.Equal(OrderStatus.PAID, order.GetEffectiveStatus(order.ExpiresAt.Value.AddDays(1)));
    }
}
