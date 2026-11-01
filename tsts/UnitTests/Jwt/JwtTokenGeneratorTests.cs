using Infra.Jwt.Base.Configuration;
using Infra.Jwt.Base.TokenGenerator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace UnitTests.Jwt;

public sealed class JwtTokenGeneratorTests
{
    private const string SECRET_KEY = "chave-de-teste-com-mais-de-32-bytes-de-tamanho";

    [Fact]
    public void Given_CustomerAndProducer_When_Generating_Should_WriteClaimsAndExpiration()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var producerId = Guid.NewGuid();
        var issuedAt = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
        var expiresAt = issuedAt.AddHours(4);
        var generator = new JwtTokenGenerator(new JwtConfiguration(SECRET_KEY));

        // Act
        var token = new JsonWebToken(generator.Generate(customerId, producerId, issuedAt, expiresAt));

        // Assert
        Assert.Equal(customerId.ToString(), token.Subject);
        Assert.Equal(producerId.ToString(), token.GetClaim(JwtTokenGenerator.PRODUCER_ID_CLAIM).Value);
        Assert.Equal(expiresAt, token.ValidTo);
        Assert.Equal("HS256", token.Alg);
    }

    [Fact]
    public void Given_ShortSecretKey_When_Configuring_Should_Throw()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtConfiguration:SecretKey"] = "curta" })
            .Build();

        // Act
        var configure = () => new ServiceCollection().AddJwtConfiguration(configuration);

        // Assert
        Assert.Throws<ArgumentException>(configure);
    }
}
