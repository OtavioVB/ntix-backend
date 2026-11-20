using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class OrderValueObjectsTests
{
    [Fact]
    public void Given_Generation_When_Generating_Should_UsePrefixAndAlphabetWithoutAmbiguousCharacters()
    {
        // Arrange & Act
        var codes = Enumerable.Range(0, 2000).Select(_ => OrderCodeValueObject.Generate().Value).ToArray();

        // Assert
        Assert.All(codes, code =>
        {
            Assert.Equal(10, code.Length);
            Assert.StartsWith("NX", code);
            Assert.All(code[2..], character => Assert.Contains(character, OrderCodeValueObject.ALPHABET));
        });
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void Given_CodeInLowerCaseWithSpaces_When_Building_Should_NormalizeToUpperCase()
    {
        // Arrange & Act
        var code = OrderCodeValueObject.Build("  nx7k2qh9mp ");

        // Assert
        Assert.True(code.IsValid);
        Assert.Equal("NX7K2QH9MP", code.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NX7K2QH9M")]
    [InlineData("NX7K2QH9MPP")]
    [InlineData("XX7K2QH9MP")]
    [InlineData("NX0K2QH9MP")]
    [InlineData("NX1K2QH9MP")]
    [InlineData("NXIK2QH9MP")]
    [InlineData("NXOK2QH9MP")]
    public void Given_CodeOutOfFormat_When_Building_Should_BeInvalidAndRefuseTheValue(string? value)
    {
        // Arrange & Act
        var code = OrderCodeValueObject.Build(value);

        // Assert
        Assert.False(code.IsValid);
        Assert.True(code.MethodResult.IsError);
        Assert.Throws<InvalidOperationException>(() => code.Value);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100_001, false)]
    [InlineData(null, false)]
    public void Given_Quantity_When_Building_Should_AcceptOneUpToTheBatchCeiling(int? quantity, bool expected)
    {
        // Arrange & Act
        var valueObject = OrderItemQuantityValueObject.Build(quantity);

        // Assert
        Assert.Equal(expected, valueObject.IsValid);

        if (!expected)
        {
            Assert.Equal(OrderItemQuantityValueObject.ORDER_ITEM_QUANTITY_INVALID_NOTIFICATION_CODE, Assert.Single(valueObject.MethodResult.Notifications).Code);
            Assert.Throws<InvalidOperationException>(() => valueObject.Value);
        }
    }

    [Theory]
    [InlineData("HUB", SalesChannel.HUB)]
    [InlineData("STORE", SalesChannel.STORE)]
    public void Given_KnownChannel_When_Building_Should_ParseIt(string value, SalesChannel expected)
        => Assert.Equal(expected, SalesChannelValueObject.Build(value).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hub")]
    [InlineData("AFFILIATE")]
    public void Given_UnknownChannel_When_Building_Should_ReportInvalidChannel(string? value)
    {
        // Arrange & Act
        SalesChannelValueObject channel = value;

        // Assert
        Assert.False(channel.IsValid);
        Assert.Equal(SalesChannelValueObject.ORDER_SALES_CHANNEL_INVALID_NOTIFICATION_CODE, Assert.Single(channel.MethodResult.Notifications).Code);
        Assert.Throws<InvalidOperationException>(() => channel.Value);
    }
}
