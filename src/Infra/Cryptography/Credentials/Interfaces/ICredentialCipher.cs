namespace Infra.Cryptography.Credentials.Interfaces;

/// <summary>
/// Cifragem autenticada das credenciais de pagamento. O dado associado amarra o valor cifrado ao registro dele
/// (integração e chave): um valor copiado para outra linha do banco não decifra.
/// </summary>
public interface ICredentialCipher
{
    public byte[] Encrypt(byte[] plaintext, byte[] associatedData);

    /// <exception cref="System.Security.Cryptography.AuthenticationTagMismatchException">Valor adulterado, de outro registro ou cifrado com outra chave.</exception>
    public byte[] Decrypt(byte[] encrypted, byte[] associatedData);
}
