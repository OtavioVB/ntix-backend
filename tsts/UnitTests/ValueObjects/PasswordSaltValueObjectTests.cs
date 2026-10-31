using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class PasswordSaltValueObjectTests
{
    [Fact]
    public void Given_NewSalt_When_Building_Should_HaveExpectedLength()
    {
        // Arrange & Act
        var salt = PasswordSaltValueObject.Build();

        // Assert
        Assert.True(salt.IsValid);
        Assert.Equal(PasswordSaltValueObject.LENGTH, salt.Value.Length);
    }

    [Fact]
    public void Given_TwoSalts_When_Building_Should_BeDifferent()
    {
        // Arrange & Act
        var first = PasswordSaltValueObject.Build();
        var second = PasswordSaltValueObject.Build();

        // Assert
        Assert.NotEqual(first.Value, second.Value);
    }
}
