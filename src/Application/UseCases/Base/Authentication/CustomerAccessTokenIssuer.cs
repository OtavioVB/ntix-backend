using Application.UseCases.Base.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Application.UseCases.Base.Authentication;

/// <summary>
/// Emite o access token do cliente (REQ12): JWT HS256 com a mesma <see cref="JwtConfiguration"/> do produtor (emissor,
/// audiência, chave e validade de 15 minutos), com <c>sub</c>, <c>actor = CUSTOMER</c> e <c>jti</c> (id da sessão).
/// </summary>
public sealed class CustomerAccessTokenIssuer
{
    private readonly JwtConfiguration _configuration;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public CustomerAccessTokenIssuer(JwtConfiguration configuration)
    {
        _configuration = configuration;
        _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(configuration.GetValidatedSigningKey()), SecurityAlgorithms.HmacSha256);
    }

    /// <summary>Validade do access token em segundos, como sai em <c>accessTokenExpiresIn</c>.</summary>
    public int ExpiresInSeconds => (int)_configuration.AccessTokenLifetime.TotalSeconds;

    public string Issue(Guid customerId, Guid sessionId, DateTime now)
        => _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _configuration.Issuer,
            Audience = _configuration.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(_configuration.AccessTokenLifetime),
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [CustomerClaims.SUBJECT] = customerId.ToString(),
                [CustomerClaims.ACTOR] = CustomerClaims.CUSTOMER_ACTOR,
                [CustomerClaims.SESSION_ID] = sessionId.ToString(),
            },
        });
}
