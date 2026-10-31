using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class CpfValueObjectTests
{
    [Theory]
    [InlineData("52998224725", "52998224725")]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData(" 111.444.777-35 ", "11144477735")]
    public void Given_ValidCpf_When_Building_Should_ReturnOnlyDigits(string cpf, string expected)
    {
        // Arrange & Act
        var valueObject = CpfValueObject.Build(cpf);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_EmptyCpf_When_Building_Should_ReturnEmptyNotification(string? cpf)
    {
        // Arrange & Act
        var valueObject = CpfValueObject.Build(cpf);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("CPF_COULD_NOT_BE_EMPTY", notification.Code);
    }

    [Theory]
    [InlineData("52998224724")]
    [InlineData("52998224715")]
    [InlineData("11111111111")]
    [InlineData("5299822472")]
    [InlineData("529982247250")]
    [InlineData("52998224A25")]
    [InlineData("529/982/247-25")]
    public void Given_InvalidCpf_When_Building_Should_ReturnFormatNotification(string cpf)
    {
        // Arrange & Act
        var valueObject = CpfValueObject.Build(cpf);

        // Assert
        Assert.False(valueObject.IsValid);
        var notification = Assert.Single(valueObject.MethodResult.Notifications);
        Assert.Equal("CPF_FORMAT_INVALID", notification.Code);
    }

    [Theory]
    [InlineData("52998224725")]
    [InlineData("529.982.247-25")]
    public void Given_ValidCpf_When_Obfuscating_Should_HideFirstAndLastDigits(string cpf)
    {
        // Arrange
        var valueObject = CpfValueObject.Build(cpf);

        // Act
        var obfuscated = valueObject.GetObfuscatedValue();

        // Assert
        Assert.Equal("***.982.247-**", obfuscated);
    }

    [Fact]
    public void Given_InvalidCpf_When_Obfuscating_Should_ThrowInvalidOperationException()
    {
        // Arrange
        var valueObject = CpfValueObject.Build("123");

        // Act
        var obfuscate = () => valueObject.GetObfuscatedValue();

        // Assert
        Assert.Throws<InvalidOperationException>(obfuscate);
    }
}
