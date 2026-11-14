using Infra.Cryptography.Base.Configuration;
using Infra.Cryptography.Credentials.Interfaces;
using System.Security.Cryptography;

namespace Infra.Cryptography.Credentials;

/// <summary>
/// AES-256-GCM da biblioteca padrão, com nonce aleatório por valor (RN9). Formato gravado:
/// nonce (12 bytes) + tag (16 bytes) + texto cifrado.
/// </summary>
public sealed class CredentialCipher : ICredentialCipher
{
    public const int KEY_SIZE_IN_BYTES = 32;
    public const int NONCE_SIZE_IN_BYTES = 12;
    public const int TAG_SIZE_IN_BYTES = 16;

    private readonly byte[] _key;

    public CredentialCipher(PaymentCredentialConfiguration configuration)
    {
        _key = DecodeKey(configuration.EncryptionKey);
    }

    /// <summary>Lê a chave em Base64 e exige exatamente 32 bytes, para a API não subir com uma chave fraca ou truncada.</summary>
    public static byte[] DecodeKey(string? encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(encryptionKey))
            throw new InvalidOperationException("PaymentCredentialConfiguration:EncryptionKey is required.");

        byte[] key;

        try
        {
            key = Convert.FromBase64String(encryptionKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("PaymentCredentialConfiguration:EncryptionKey must be Base64.");
        }

        if (key.Length != KEY_SIZE_IN_BYTES)
            throw new InvalidOperationException($"PaymentCredentialConfiguration:EncryptionKey must have {KEY_SIZE_IN_BYTES} bytes.");

        return key;
    }

    public byte[] Encrypt(byte[] plaintext, byte[] associatedData)
    {
        var encrypted = new byte[NONCE_SIZE_IN_BYTES + TAG_SIZE_IN_BYTES + plaintext.Length];

        var nonce = encrypted.AsSpan(0, NONCE_SIZE_IN_BYTES);
        var tag = encrypted.AsSpan(NONCE_SIZE_IN_BYTES, TAG_SIZE_IN_BYTES);
        var ciphertext = encrypted.AsSpan(NONCE_SIZE_IN_BYTES + TAG_SIZE_IN_BYTES);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TAG_SIZE_IN_BYTES);

        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return encrypted;
    }

    public byte[] Decrypt(byte[] encrypted, byte[] associatedData)
    {
        if (encrypted.Length <= NONCE_SIZE_IN_BYTES + TAG_SIZE_IN_BYTES)
            throw new CryptographicException("The encrypted credential is too short.");

        var nonce = encrypted.AsSpan(0, NONCE_SIZE_IN_BYTES);
        var tag = encrypted.AsSpan(NONCE_SIZE_IN_BYTES, TAG_SIZE_IN_BYTES);
        var ciphertext = encrypted.AsSpan(NONCE_SIZE_IN_BYTES + TAG_SIZE_IN_BYTES);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TAG_SIZE_IN_BYTES);

        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }
}
