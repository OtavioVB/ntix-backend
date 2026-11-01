namespace Infra.Jwt.Base.TokenGenerator.Interfaces;

public interface IJwtTokenGenerator
{
    public string Generate(Guid customerId, Guid producerId, DateTime issuedAt, DateTime expiresAt);
}
