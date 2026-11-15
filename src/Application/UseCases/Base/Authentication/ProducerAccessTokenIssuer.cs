using Application.UseCases.Base.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Application.UseCases.Base.Authentication;

/// <summary>
/// Emite o access token do usuário do produtor (REQ5): JWT HS256 de 15 minutos com <c>iss</c>, <c>aud</c>, <c>sub</c>,
/// <c>producer_id</c>, <c>producer_slug</c>, <c>user_type</c> e <c>jti</c> (id da sessão). Só identificadores (RN5).
/// </summary>
public sealed class ProducerAccessTokenIssuer
{
    private readonly JwtConfiguration _configuration;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public ProducerAccessTokenIssuer(JwtConfiguration configuration)
    {
        _configuration = configuration;
        _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(configuration.GetValidatedSigningKey()), SecurityAlgorithms.HmacSha256);
    }

    /// <summary>Validade do access token em segundos, como sai em <c>accessTokenExpiresIn</c>.</summary>
    public int ExpiresInSeconds => (int)_configuration.AccessTokenLifetime.TotalSeconds;

    public string Issue(Guid producerUserId, Guid producerId, string producerSlug, string userType, Guid sessionId, DateTime now)
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
                [ProducerClaims.SUBJECT] = producerUserId.ToString(),
                [ProducerClaims.PRODUCER_ID] = producerId.ToString(),
                [ProducerClaims.PRODUCER_SLUG] = producerSlug,
                [ProducerClaims.USER_TYPE] = userType,
                [ProducerClaims.SESSION_ID] = sessionId.ToString(),
            },
        });
}
