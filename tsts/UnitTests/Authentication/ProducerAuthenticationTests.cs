using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using UnitTests.Support;
using WebApi.Authentication;

namespace UnitTests.Authentication;

public sealed class ProducerAuthenticationTests
{
    private static readonly DateTime NOW = ProducerSessionTestData.NOW.UtcDateTime;

    private readonly ProducerAccessTokenIssuer _issuer = new(ProducerSessionTestData.JWT_CONFIGURATION);

    private static string IssueFor(ProducerAccessTokenIssuer issuer, Guid userId, Guid sessionId)
        => issuer.Issue(
            producerUserId: userId,
            producerId: ProducerSessionTestData.PRODUCER_ID,
            producerSlug: ProducerSessionTestData.PRODUCER_SLUG,
            userType: "ADMINISTRATOR",
            sessionId: sessionId,
            now: NOW);

    [Fact]
    public async Task Given_ProducerUser_When_IssuingAccessToken_Should_CarryOnlyIdentifiersSignedWithHs256For15Minutes()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Act
        var token = IssueFor(_issuer, userId, sessionId);

        // Assert
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = ProducerSessionTestData.JWT_CONFIGURATION.Issuer,
            ValidAudience = ProducerSessionTestData.JWT_CONFIGURATION.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(ProducerSessionTestData.JWT_CONFIGURATION.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = false,
        });

        Assert.True(validation.IsValid);

        var jwt = new JsonWebToken(token);

        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal(ProducerSessionTestData.PRODUCER_ID.ToString(), jwt.GetClaim(ProducerClaims.PRODUCER_ID).Value);
        Assert.Equal(ProducerSessionTestData.PRODUCER_SLUG, jwt.GetClaim(ProducerClaims.PRODUCER_SLUG).Value);
        Assert.Equal("ADMINISTRATOR", jwt.GetClaim(ProducerClaims.USER_TYPE).Value);
        Assert.Equal(sessionId.ToString(), jwt.Id);
        Assert.Equal(NOW, jwt.IssuedAt);
        Assert.Equal(NOW.AddMinutes(15), jwt.ValidTo);
        Assert.Equal(900, _issuer.ExpiresInSeconds);
    }

    [Fact]
    public void Given_AccessToken_When_ReadingThePayload_Should_NotContainPersonalData()
    {
        // Arrange
        var token = IssueFor(_issuer, Guid.NewGuid(), Guid.NewGuid());

        // Act: o payload do JWT é só Base64Url, legível por quem tiver o token (RN5).
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));

        // Assert
        Assert.DoesNotContain(ProducerSessionTestData.EMAIL, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(ProducerSessionTestData.USER_NAME, payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Produtora X", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bm8tYmFzZTY0")]
    [InlineData("não é base64")]
    public void Given_SigningKeyMissingOrShorterThan32Bytes_When_Validating_Should_Throw(string? signingKey)
    {
        // Arrange
        var configuration = ProducerSessionTestData.JWT_CONFIGURATION with { SigningKey = signingKey! };

        // Act + Assert
        Assert.ThrowsAny<Exception>(() => configuration.GetValidatedSigningKey());
    }

    [Theory]
    [InlineData("produtora-x", "produtora-x", true)]
    [InlineData("produtora-x", "produtora-y", false)]
    [InlineData("produtora-x", "Produtora-X", false)]
    [InlineData("produtora-x", null, false)]
    [InlineData(null, "produtora-x", false)]
    public async Task Given_TokenAndRouteSlugs_When_Authorizing_Should_OnlyAllowTheProducerOfTheToken(string? tokenSlug, string? routeSlug, bool expected)
    {
        // Arrange
        var claims = tokenSlug is null ? [] : new[] { new Claim(ProducerClaims.PRODUCER_SLUG, tokenSlug) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var httpContext = new DefaultHttpContext();
        if (routeSlug is not null)
            httpContext.Request.RouteValues = new RouteValueDictionary { [ProducerSlugRequirement.ROUTE_VALUE_KEY] = routeSlug };

        var requirement = new ProducerSlugRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, httpContext);

        // Act
        await new ProducerSlugAuthorizationHandler().HandleAsync(context);

        // Assert
        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task Given_ResourceThatIsNotAnHttpRequest_When_Authorizing_Should_Deny()
    {
        // Arrange: sem requisição HTTP não há slug de rota para comparar.
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ProducerClaims.PRODUCER_SLUG, "produtora-x")], "Bearer"));
        var requirement = new ProducerSlugRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: "produtora-x");

        // Act
        await new ProducerSlugAuthorizationHandler().HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void Given_ConfigurationWithoutDurations_When_Reading_Should_Default15MinutesAnd7Days()
    {
        // Arrange
        var configuration = new JwtConfiguration(SigningKey: ProducerSessionTestData.JWT_CONFIGURATION.SigningKey, Issuer: "iss", Audience: "aud");

        // Act + Assert
        Assert.Equal(TimeSpan.FromMinutes(15), configuration.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), configuration.RefreshTokenLifetime);
    }
}
