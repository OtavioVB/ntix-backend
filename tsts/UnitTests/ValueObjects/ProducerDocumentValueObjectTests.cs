using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class ProducerDocumentValueObjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_NoDocument_When_Building_Should_BeValidWithoutValue(string? document)
    {
        // Act
        var valueObject = ProducerDocumentValueObject.Build(document);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Null(valueObject.Value);
    }

    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("12.abc.345/01de-35", "12ABC34501DE35")]
    public void Given_ValidDocument_When_Building_Should_Normalize(string document, string expected)
    {
        // Act
        var valueObject = ProducerDocumentValueObject.Build(document);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData("52998224724")]
    [InlineData("11222333000182")]
    [InlineData("11111111111111")]
    [InlineData("12ABC34501DE3X")]
    [InlineData("123")]
    public void Given_InvalidDocument_When_Building_Should_ReturnNotification(string document)
    {
        // Act
        var valueObject = ProducerDocumentValueObject.Build(document);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("PRODUCER_DOCUMENT_INVALID", Assert.Single(valueObject.MethodResult.Notifications).Code);
    }
}
