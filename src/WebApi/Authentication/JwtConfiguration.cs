namespace WebApi.Authentication;

/// <summary>
/// Validação do access token do usuário do produtor (REQ5, RFC 0007). <see cref="SigningKey"/> é Base64 de 32 bytes
/// ou mais e vem de <c>JwtConfiguration__SigningKey</c>: a API não sobe sem ela.
/// </summary>
public sealed record JwtConfiguration(string SigningKey, string Issuer, string Audience);
