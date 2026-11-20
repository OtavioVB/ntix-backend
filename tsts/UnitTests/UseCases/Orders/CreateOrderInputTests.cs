using Application.UseCases.CreateOrder.Inputs;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.UseCases.Orders;

/// <summary>Validação do corpo do pedido (REQ6, RN2 e RN3): uma notificação por regra violada.</summary>
public sealed class CreateOrderInputTests
{
    private static readonly string BATCH = TicketBatchTestData.BATCH_ID.ToString();

    private static string[] CodesOf(CreateOrderUseCaseInput input)
        => input.GetInputValidation().Notifications.Select(notification => notification.Code).ToArray();

    [Fact]
    public void Given_CompleteOrder_When_Validating_Should_Succeed()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), " festival-horizonte ", "STORE", [(BATCH, 2)]);

        // Act & Assert
        Assert.True(input.GetInputValidation().IsSuccess);
        Assert.Equal("festival-horizonte", input.EventSlug);
    }

    [Fact]
    public void Given_NoCustomerInTheToken_When_Validating_Should_ReportCustomerRequired()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create("nao-e-guid", EventTestData.SLUG, "HUB", [(BATCH, 1)]);

        // Act & Assert
        Assert.Equal(CreateOrderUseCaseInput.ORDER_CUSTOMER_REQUIRED_NOTIFICATION_CODE, Assert.Single(CodesOf(input)));
    }

    [Fact]
    public void Given_EmptyItems_When_Validating_Should_ReportItemsRequired()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), EventTestData.SLUG, "HUB", []);

        // Act & Assert
        Assert.Equal(CreateOrderUseCaseInput.ORDER_ITEMS_REQUIRED_NOTIFICATION_CODE, Assert.Single(CodesOf(input)));
    }

    [Fact]
    public void Given_SameBatchTwice_When_Validating_Should_ReportDuplicatedItem()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), EventTestData.SLUG, "HUB", [(BATCH, 1), (BATCH, 2)]);

        // Act & Assert
        Assert.Equal(CreateOrderUseCaseInput.ORDER_ITEM_DUPLICATED_NOTIFICATION_CODE, Assert.Single(CodesOf(input)));
    }

    [Fact]
    public void Given_SeveralInvalidQuantities_When_Validating_Should_ReportQuantityOnce()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), EventTestData.SLUG, "HUB", [(BATCH, 0), (OrderTestData.VIP_BATCH_ID.ToString(), null)]);

        // Act & Assert
        Assert.Equal(OrderItemQuantityValueObject.ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_CODE, Assert.Single(CodesOf(input)));
    }

    [Fact]
    public void Given_UnparseableBatchId_When_Validating_Should_AcceptItAndLetTheBatchBeNotFound()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), EventTestData.SLUG, "HUB", [("lote-1", 1)]);

        // Act & Assert
        Assert.True(input.GetInputValidation().IsSuccess);
        Assert.Equal(Guid.Empty, Assert.Single(input.Items).BatchId);
    }

    [Fact]
    public void Given_UnknownChannel_When_Validating_Should_ReportSalesChannelInvalid()
    {
        // Arrange
        var input = CreateOrderUseCaseInput.Create(OrderTestData.CUSTOMER_ID.ToString(), EventTestData.SLUG, "AFFILIATE", [(BATCH, 1)]);

        // Act & Assert
        Assert.Equal(SalesChannelValueObject.ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_CODE, Assert.Single(CodesOf(input)));
    }
}
