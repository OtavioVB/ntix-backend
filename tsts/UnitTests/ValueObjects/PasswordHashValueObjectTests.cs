using Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace UnitTests.ValueObjects;

public sealed class PasswordHashValueObjectTests
{
    private const string PASSWORD = "Senha@123";
    private const string PRIVATE_KEY = "chave-de-teste";

    [Fact]
    public void Given_PasswordSaltAndKey_When_Building_Should_CombinePrivateKeyWithSaltedPasswordHash()
    {
        // Arrange
        var salt = PasswordSaltValueObject.Build();
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(PASSWORD, salt.Value, 600_000, HashAlgorithmName.SHA256, 32);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(PRIVATE_KEY), passwordHash);

        // Act
        var hash = PasswordHashValueObject.Build(PASSWORD, salt, PRIVATE_KEY);

        // Assert
        Assert.True(hash.IsValid);
        Assert.Equal(expected, hash.Value);
    }

    [Fact]
    public void Given_SameInputs_When_Building_Should_ReturnSameHash()
    {
        // Arrange
        var salt = PasswordSaltValueObject.Build();

        // Act
        var first = PasswordHashValueObject.Build(PASSWORD, salt, PRIVATE_KEY);
        var second = PasswordHashValueObject.Build(PASSWORD, salt, PRIVATE_KEY);

        // Assert
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public void Given_DifferentSalts_When_Building_Should_ReturnDifferentHashes()
    {
        // Arrange & Act
        var first = PasswordHashValueObject.Build(PASSWORD, PasswordSaltValueObject.Build(), PRIVATE_KEY);
        var second = PasswordHashValueObject.Build(PASSWORD, PasswordSaltValueObject.Build(), PRIVATE_KEY);

        // Assert
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Given_DifferentPrivateKeys_When_Building_Should_ReturnDifferentHashes()
    {
        // Arrange
        var salt = PasswordSaltValueObject.Build();

        // Act
        var first = PasswordHashValueObject.Build(PASSWORD, salt, PRIVATE_KEY);
        var second = PasswordHashValueObject.Build(PASSWORD, salt, "outra-chave");

        // Assert
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Given_InvalidPassword_When_Building_Should_Throw()
    {
        // Arrange
        var salt = PasswordSaltValueObject.Build();

        // Act
        Action build = () => PasswordHashValueObject.Build("invalida", salt, PRIVATE_KEY);

        // Assert
        Assert.Throws<InvalidOperationException>(build);
    }
}
