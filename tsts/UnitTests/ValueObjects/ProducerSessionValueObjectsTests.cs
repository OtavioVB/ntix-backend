using Domain.ValueObjects;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace UnitTests.ValueObjects;

public sealed class ProducerSessionValueObjectsTests
{
    [Fact]
    public void Given_NewRefreshToken_When_Generating_Should_Have32RandomBytesInBase64UrlAndSha256Hash()
    {
        // Act
        var first = RefreshTokenValueObject.Generate();
        var second = RefreshTokenValueObject.Generate();

        // Assert
        Assert.True(first.IsValid);
        Assert.Equal(43, first.Value.Length);
        Assert.Equal(RefreshTokenValueObject.LENGTH, Base64Url.DecodeFromChars(first.Value).Length);
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(first.Value)), first.Hash);
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Given_GeneratedToken_When_BuildingFromItsValue_Should_HaveTheSameHash()
    {
        // Arrange
        var generated = RefreshTokenValueObject.Generate();

        // Act
        var received = RefreshTokenValueObject.Build(generated.Value);

        // Assert
        Assert.True(received.IsValid);
        Assert.Equal(generated.Hash, received.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curto")]
    [InlineData("mF3q+não-é-base64url/=====================")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Given_TokenOutOfFormat_When_Building_Should_BeInvalid(string? token)
    {
        // Act
        var refreshToken = RefreshTokenValueObject.Build(token);

        // Assert
        Assert.False(refreshToken.IsValid);
        Assert.Contains(refreshToken.MethodResult.Notifications, notification => notification.Code == "REFRESH_TOKEN_INVALID");
    }

    [Fact]
    public void Given_NonCanonicalEncoding_When_Building_Should_BeInvalid()
    {
        // Arrange: o último caractere de 43 carrega 2 bits sobrando; trocá-lo pode decodificar nos mesmos bytes.
        var generated = RefreshTokenValueObject.Generate().Value;
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var lastIndex = alphabet.IndexOf(generated[^1]);
        var sibling = generated[..^1] + alphabet[lastIndex ^ 1];

        // Act
        var refreshToken = RefreshTokenValueObject.Build(sibling);

        // Assert
        Assert.False(refreshToken.IsValid && refreshToken.Hash.SequenceEqual(RefreshTokenValueObject.Build(generated).Hash));
    }

    [Theory]
    [InlineData("senha")]
    [InlineData("SÓ-MAIÚSCULAS")]
    public void Given_PasswordOutsideStrengthRule_When_BuildingForSignIn_Should_BeValid(string password)
    {
        // Act
        var signInPassword = PasswordValueObject.BuildForSignIn(password);

        // Assert
        Assert.True(signInPassword.IsValid);
        Assert.Equal(password, signInPassword.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Given_EmptyPassword_When_BuildingForSignIn_Should_ReturnEmptyPasswordNotification(string? password)
    {
        // Act
        var signInPassword = PasswordValueObject.BuildForSignIn(password);

        // Assert
        Assert.False(signInPassword.IsValid);
        Assert.Contains(signInPassword.MethodResult.Notifications, notification => notification.Code == "PASSWORD_COULD_NOT_BE_EMPTY");
    }

    [Fact]
    public void Given_SamePasswordAndSalt_When_ComparingHashes_Should_Match()
    {
        // Arrange
        var salt = PasswordSaltValueObject.Build();
        var stored = PasswordHashValueObject.Build(PasswordValueObject.Build("Senha@123"), salt, "chave");

        // Act
        var typed = PasswordHashValueObject.Build(PasswordValueObject.BuildForSignIn("Senha@123"), PasswordSaltValueObject.Build(salt.Value), "chave");
        var wrong = PasswordHashValueObject.Build(PasswordValueObject.BuildForSignIn("Errada@123"), PasswordSaltValueObject.Build(salt.Value), "chave");

        // Assert
        Assert.True(typed.Matches(stored.Value));
        Assert.False(wrong.Matches(stored.Value));
        Assert.False(typed.Matches(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(15)]
    [InlineData(17)]
    public void Given_StoredSaltOutOfSize_When_Building_Should_BeInvalid(int? length)
    {
        // Act
        var salt = PasswordSaltValueObject.Build(length is null ? null : new byte[length.Value]);

        // Assert
        Assert.False(salt.IsValid);
    }

    [Fact]
    public void Given_InvalidValueObjects_When_ReadingTheValue_Should_Throw()
    {
        // Arrange
        var refreshToken = RefreshTokenValueObject.Build("inválido");
        var salt = PasswordSaltValueObject.Build((byte[]?)null);
        var hash = default(PasswordHashValueObject);

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => refreshToken.Value);
        Assert.Throws<InvalidOperationException>(() => refreshToken.Hash);
        Assert.Throws<InvalidOperationException>(() => salt.Value);
        Assert.Throws<InvalidOperationException>(() => hash.Value);
        Assert.False(hash.Matches(new byte[PasswordHashValueObject.LENGTH]));
    }

    [Fact]
    public void Given_TokenText_When_ConvertingImplicitly_Should_BuildTheValueObject()
    {
        // Arrange
        var generated = RefreshTokenValueObject.Generate().Value;

        // Act
        RefreshTokenValueObject converted = generated;

        // Assert
        Assert.True(converted.IsValid);
        Assert.Equal(generated, converted.Value);
    }

    [Fact]
    public void Given_UnknownUser_When_BuildingSalt_Should_AlwaysBeTheSameFixedSalt()
    {
        // Act
        var first = PasswordSaltValueObject.BuildForUnknownUser();
        var second = PasswordSaltValueObject.BuildForUnknownUser();

        // Assert
        Assert.Equal(PasswordSaltValueObject.LENGTH, first.Value.Length);
        Assert.Equal(first.Value, second.Value);
    }
}
