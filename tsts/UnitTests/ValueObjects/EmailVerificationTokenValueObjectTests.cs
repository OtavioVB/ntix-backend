using Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace UnitTests.ValueObjects;

public sealed class EmailVerificationTokenValueObjectTests
{
    [Fact]
    public void Given_TwoTokens_When_Building_Should_GenerateDifferentValues()
    {
        // Act
        var first = EmailVerificationTokenValueObject.Build();
        var second = EmailVerificationTokenValueObject.Build();

        // Assert
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Given_Token_When_Building_Should_BeSafeForUrl()
        => Assert.Matches("^[a-f0-9]{64}$", EmailVerificationTokenValueObject.Build().Value);

    [Fact]
    public void Given_Token_When_GettingHash_Should_ReturnSha256OfValue()
    {
        // Arrange
        var token = EmailVerificationTokenValueObject.Build();

        // Act
        var hash = token.GetHash();

        // Assert
        Assert.Equal(EmailVerificationTokenValueObject.HASH_LENGTH, hash.Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value)), hash);
    }
}
