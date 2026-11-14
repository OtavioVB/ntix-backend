using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class IntegrationNameValueObjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyName_When_Building_Should_ReturnIntegrationNameInvalid(string? name)
    {
        // Arrange & Act
        var valueObject = IntegrationNameValueObject.Build(name);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "INTEGRATION_NAME_INVALID");
    }

    [Fact]
    public void Given_NameWith65Characters_When_Building_Should_ReturnIntegrationNameInvalid()
    {
        // Arrange
        var name = new string('a', IntegrationNameValueObject.MAX_LENGTH + 1);

        // Act
        var valueObject = IntegrationNameValueObject.Build(name);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "INTEGRATION_NAME_INVALID");
    }

    [Fact]
    public void Given_NameWith64Characters_When_Building_Should_BeValid()
    {
        // Arrange
        var name = new string('a', IntegrationNameValueObject.MAX_LENGTH);

        // Act
        var valueObject = IntegrationNameValueObject.Build(name);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(name, valueObject.Value);
    }

    [Fact]
    public void Given_NameWithRepeatedSpaces_When_Building_Should_NormalizeSpaces()
    {
        // Arrange & Act
        var valueObject = IntegrationNameValueObject.Build("  AbacatePay  -   conta principal ");

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal("AbacatePay - conta principal", valueObject.Value);
    }
}

public sealed class CredentialValueObjectTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" abc_dev_123")]
    [InlineData("abc_dev_123 ")]
    [InlineData("abc_dev_123\n")]
    public void Given_EmptyOrUntrimmedValue_When_Building_Should_ReturnCredentialValueInvalid(string? value)
    {
        // Arrange & Act
        var valueObject = CredentialValueObject.Build(value);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "CREDENTIAL_VALUE_INVALID");
    }

    [Fact]
    public void Given_ValueWith2049Characters_When_Building_Should_ReturnCredentialValueInvalid()
    {
        // Arrange & Act
        var valueObject = CredentialValueObject.Build(new string('k', CredentialValueObject.MAX_LENGTH + 1));

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "CREDENTIAL_VALUE_INVALID");
    }

    [Fact]
    public void Given_ValueWith2048Characters_When_Building_Should_BeValid()
    {
        // Arrange & Act
        var valueObject = CredentialValueObject.Build(new string('k', CredentialValueObject.MAX_LENGTH));

        // Assert
        Assert.True(valueObject.IsValid);
    }

    [Fact]
    public void Given_ValueWithInnerSpace_When_Building_Should_KeepValueAsTyped()
    {
        // Arrange & Act
        var valueObject = CredentialValueObject.Build("chave com espaço");

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal("chave com espaço", valueObject.Value);
    }

    [Fact]
    public void Given_LongValue_When_GettingObfuscatedValue_Should_ReturnOnlyLastFourCharacters()
    {
        // Arrange
        var valueObject = CredentialValueObject.Build("abc_prod_7f3e9a1b2c7c2e");

        // Act
        var hint = valueObject.GetObfuscatedValue();

        // Assert
        Assert.Equal("7c2e", hint);
    }

    [Theory]
    [InlineData("abcd", "cd")]
    [InlineData("abcdefg", "efg")]
    [InlineData("a", "")]
    public void Given_ShortValue_When_GettingObfuscatedValue_Should_NeverRevealMoreThanHalf(string value, string expectedHint)
    {
        // Arrange
        var valueObject = CredentialValueObject.Build(value);

        // Act
        var hint = valueObject.GetObfuscatedValue();

        // Assert
        Assert.Equal(expectedHint, hint);
    }
}

public sealed class PaymentMethodValueObjectTests
{
    [Theory]
    [InlineData("PIX", PaymentMethod.PIX)]
    [InlineData("BOLETO", PaymentMethod.BOLETO)]
    [InlineData("CREDIT_CARD", PaymentMethod.CREDIT_CARD)]
    public void Given_KnownMethod_When_Building_Should_ReturnMethod(string value, PaymentMethod expected)
    {
        // Arrange & Act
        var valueObject = PaymentMethodValueObject.Build(value);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pix")]
    [InlineData("1")]
    [InlineData("CARD")]
    public void Given_UnknownMethod_When_Building_Should_ReturnPaymentMethodInvalid(string? value)
    {
        // Arrange & Act
        var valueObject = PaymentMethodValueObject.Build(value);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "PAYMENT_METHOD_INVALID");
    }
}

public sealed class IntegrationStatusValueObjectTests
{
    [Theory]
    [InlineData("ACTIVE", IntegrationStatus.ACTIVE)]
    [InlineData("INACTIVE", IntegrationStatus.INACTIVE)]
    public void Given_KnownStatus_When_Building_Should_ReturnStatus(string value, IntegrationStatus expected)
    {
        // Arrange & Act
        var valueObject = IntegrationStatusValueObject.Build(value);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("DELETED")]
    [InlineData("2")]
    public void Given_UnknownStatus_When_Building_Should_ReturnIntegrationStatusInvalid(string value)
    {
        // Arrange & Act
        var valueObject = IntegrationStatusValueObject.Build(value);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "INTEGRATION_STATUS_INVALID");
    }
}
