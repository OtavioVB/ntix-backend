using Infra.Cryptography.Base.Configuration;
using Infra.Cryptography.Credentials;
using System.Security.Cryptography;
using System.Text;

namespace UnitTests.Cryptography;

public sealed class CredentialCipherTests
{
    private static readonly string KEY = Convert.ToBase64String(RandomNumberGenerator.GetBytes(CredentialCipher.KEY_SIZE_IN_BYTES));

    private static readonly byte[] PLAINTEXT = Encoding.UTF8.GetBytes("abc_prod_chave-secreta-a91f");
    private static readonly byte[] ASSOCIATED_DATA = Encoding.UTF8.GetBytes("integracao:API_KEY");

    private readonly CredentialCipher _cipher = new(new PaymentCredentialConfiguration(KEY));

    [Fact]
    public void Given_Value_When_EncryptingAndDecrypting_Should_ReturnOriginalValue()
    {
        // Arrange
        var encrypted = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);

        // Act
        var decrypted = _cipher.Decrypt(encrypted, ASSOCIATED_DATA);

        // Assert
        Assert.Equal(PLAINTEXT, decrypted);
        Assert.Equal(CredentialCipher.NONCE_SIZE_IN_BYTES + CredentialCipher.TAG_SIZE_IN_BYTES + PLAINTEXT.Length, encrypted.Length);
        Assert.DoesNotContain("abc_prod", Encoding.UTF8.GetString(encrypted));
    }

    [Fact]
    public void Given_SameValueTwice_When_Encrypting_Should_UseDifferentNonces()
    {
        // Arrange & Act
        var first = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);
        var second = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);

        // Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first[..CredentialCipher.NONCE_SIZE_IN_BYTES], second[..CredentialCipher.NONCE_SIZE_IN_BYTES]);
    }

    [Fact]
    public void Given_ValueOfAnotherRecord_When_Decrypting_Should_Fail()
    {
        // Arrange
        var encrypted = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);

        // Act & Assert
        Assert.ThrowsAny<CryptographicException>(() => _cipher.Decrypt(encrypted, Encoding.UTF8.GetBytes("outra-integracao:API_KEY")));
    }

    [Fact]
    public void Given_TamperedValue_When_Decrypting_Should_Fail()
    {
        // Arrange
        var encrypted = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);
        encrypted[^1] ^= 0xFF;

        // Act & Assert
        Assert.ThrowsAny<CryptographicException>(() => _cipher.Decrypt(encrypted, ASSOCIATED_DATA));
    }

    [Fact]
    public void Given_AnotherKey_When_Decrypting_Should_Fail()
    {
        // Arrange
        var encrypted = _cipher.Encrypt(PLAINTEXT, ASSOCIATED_DATA);
        var otherCipher = new CredentialCipher(new PaymentCredentialConfiguration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(CredentialCipher.KEY_SIZE_IN_BYTES))));

        // Act & Assert
        Assert.ThrowsAny<CryptographicException>(() => otherCipher.Decrypt(encrypted, ASSOCIATED_DATA));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-base64!")]
    [InlineData("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkw")]
    [InlineData("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODk=")]
    public void Given_KeyThatIsNot32BytesOfBase64_When_Creating_Should_Throw(string? key)
    {
        // Arrange & Act & Assert
        Assert.Throws<InvalidOperationException>(() => new CredentialCipher(new PaymentCredentialConfiguration(key!)));
    }
}
