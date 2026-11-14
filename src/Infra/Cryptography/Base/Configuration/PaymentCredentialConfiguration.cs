namespace Infra.Cryptography.Base.Configuration;

/// <summary>
/// Chave AES-256 (32 bytes em Base64) que cifra as credenciais das integrações de pagamento. Fica fora do banco:
/// vem de <c>PaymentCredentialConfiguration__EncryptionKey</c>, e a API não sobe sem ela.
/// </summary>
public sealed record PaymentCredentialConfiguration(string EncryptionKey);
