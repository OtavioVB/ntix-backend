namespace Application.UseCases.Base.Configuration;

/// <summary>
/// Sessão do usuário do produtor (REQ5, RFC 0007): emissão e validação do access token e validade do refresh token.
/// <see cref="SigningKey"/> é Base64 de 32 bytes ou mais e vem de <c>JwtConfiguration__SigningKey</c>: a API não sobe
/// sem ela.
/// </summary>
public sealed record JwtConfiguration(string SigningKey, string Issuer, string Audience, int AccessTokenMinutes = 15, int RefreshTokenDays = 7)
{
    public const int SIGNING_KEY_MIN_SIZE_IN_BYTES = 32;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);

    /// <summary>Confere a configuração inteira e devolve a chave decodificada; falha na subida, nunca na requisição.</summary>
    public byte[] GetValidatedSigningKey()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(Audience);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(AccessTokenMinutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RefreshTokenDays);

        if (string.IsNullOrWhiteSpace(SigningKey))
            throw new InvalidOperationException("JwtConfiguration:SigningKey is required.");

        byte[] key;

        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("JwtConfiguration:SigningKey must be Base64.");
        }

        if (key.Length < SIGNING_KEY_MIN_SIZE_IN_BYTES)
            throw new InvalidOperationException($"JwtConfiguration:SigningKey must have at least {SIGNING_KEY_MIN_SIZE_IN_BYTES} bytes.");

        return key;
    }
}
