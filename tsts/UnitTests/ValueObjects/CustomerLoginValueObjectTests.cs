using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class CustomerLoginValueObjectTests
{
    [Theory]
    [InlineData("529.982.247-25", CustomerLoginKind.DOCUMENT, "52998224725", "***.982.247-**")]
    [InlineData(" 52998224725 ", CustomerLoginKind.DOCUMENT, "52998224725", "***.982.247-**")]
    [InlineData("Marina@Exemplo.com", CustomerLoginKind.EMAIL, "marina@exemplo.com", "ma***@exemplo.com")]
    [InlineData("123.456.789-00", CustomerLoginKind.UNKNOWN, "123.456.789-00", "***")]
    [InlineData("marina", CustomerLoginKind.UNKNOWN, "marina", "***")]
    public void Given_Login_When_Building_Should_DecideBetweenCpfAndEmail(string login, CustomerLoginKind kind, string value, string obfuscated)
    {
        // Arrange & Act
        var valueObject = CustomerLoginValueObject.Build(login);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(kind, valueObject.Kind);
        Assert.Equal(value, valueObject.Value);
        Assert.Equal(obfuscated, valueObject.GetObfuscatedValue());
    }

    [Fact]
    public void Given_MaskedAndPlainCpf_When_BuildingAttemptKey_Should_ShareTheKeyAndBePrefixed()
    {
        // Arrange & Act
        var masked = CustomerLoginValueObject.Build("529.982.247-25").AttemptKey;
        var plain = CustomerLoginValueObject.Build("52998224725").AttemptKey;

        // Assert
        Assert.Equal(masked, plain);
        Assert.StartsWith("customer:", masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Given_EmptyLogin_When_Building_Should_ReportLoginRequired(string? login)
        => Assert.Equal("LOGIN_REQUIRED", Assert.Single(CustomerLoginValueObject.Build(login).MethodResult.Notifications).Code);
}
