using Infra.Jwt.Base.Configuration;
using Infra.Jwt.Base.TokenGenerator.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Infra.Jwt.Base.TokenGenerator;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    public const string ISSUER = "ntix-backend";
    public const string AUDIENCE = "ntix";
    public const string PRODUCER_ID_CLAIM = "producer_id";

    private static readonly JsonWebTokenHandler TOKEN_HANDLER = new();

    private readonly SigningCredentials _signingCredentials;

    public JwtTokenGenerator(JwtConfiguration configuration)
    {
        _signingCredentials = new SigningCredentials(
            key: new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.SecretKey)),
            algorithm: SecurityAlgorithms.HmacSha256);
    }

    public string Generate(Guid customerId, Guid producerId, DateTime issuedAt, DateTime expiresAt)
        => TOKEN_HANDLER.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ISSUER,
            Audience = AUDIENCE,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customerId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [PRODUCER_ID_CLAIM] = producerId.ToString(),
            },
        });
}
