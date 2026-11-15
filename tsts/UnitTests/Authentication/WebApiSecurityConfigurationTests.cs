using Application.UseCases.Base.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using UnitTests.Support;
using WebApi.Authentication;
using WebApi.Cors;

namespace UnitTests.Authentication;

/// <summary>Validação do access token, política do painel e CORS da WebApi (REQ5).</summary>
public sealed class WebApiSecurityConfigurationTests
{
    private static IConfiguration BuildConfiguration(string? signingKey)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtConfiguration:SigningKey"] = signingKey,
                ["JwtConfiguration:Issuer"] = ProducerSessionTestData.JWT_CONFIGURATION.Issuer,
                ["JwtConfiguration:Audience"] = ProducerSessionTestData.JWT_CONFIGURATION.Audience,
                ["CorsConfiguration:AllowedOrigins:0"] = "https://produtores.ntix.com.br",
            })
            .Build();

    private static ServiceProvider BuildServices(string? signingKey)
    {
        var services = new ServiceCollection().AddLogging();

        services.AddProducerAuthentication(BuildConfiguration(signingKey));
        services.AddCorsConfiguration(BuildConfiguration(signingKey));

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Given_ValidConfiguration_When_AddingAuthentication_Should_ValidateEverythingTheIssuerSigns()
    {
        // Arrange
        using var services = BuildServices(ProducerSessionTestData.JWT_CONFIGURATION.SigningKey);

        // Act
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        var parameters = options.TokenValidationParameters;
        Assert.False(options.MapInboundClaims);
        Assert.Equal(ProducerSessionTestData.JWT_CONFIGURATION.Issuer, parameters.ValidIssuer);
        Assert.Equal(ProducerSessionTestData.JWT_CONFIGURATION.Audience, parameters.ValidAudience);
        Assert.Equal([SecurityAlgorithms.HmacSha256], parameters.ValidAlgorithms);
        Assert.True(parameters.ValidateIssuer && parameters.ValidateAudience && parameters.ValidateLifetime && parameters.RequireExpirationTime);
        Assert.Equal(TimeSpan.FromSeconds(30), parameters.ClockSkew);
        Assert.Equal(ProducerClaims.SUBJECT, parameters.NameClaimType);
    }

    [Fact]
    public async Task Given_ValidConfiguration_When_ReadingThePanelPolicy_Should_RequireUserSlugAndTheSlugRequirement()
    {
        // Arrange
        using var services = BuildServices(ProducerSessionTestData.JWT_CONFIGURATION.SigningKey);

        // Act
        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(AuthenticationExtensions.PRODUCER_USER_POLICY);

        // Assert
        Assert.NotNull(policy);
        Assert.Contains(JwtBearerDefaults.AuthenticationScheme, policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, requirement => requirement is ProducerSlugRequirement);
        Assert.Contains(services.GetServices<IAuthorizationHandler>(), handler => handler is ProducerSlugAuthorizationHandler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bm8tYmFzZTY0")]
    public void Given_MissingOrShortSigningKey_When_AddingAuthentication_Should_NotStartTheApi(string? signingKey)
    {
        // Act + Assert
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddProducerAuthentication(BuildConfiguration(signingKey)));
    }

    [Fact]
    public void Given_AuthenticatedUser_When_GettingTheExecutionUser_Should_ReturnTheSubject()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ProducerClaims.SUBJECT, userId)], "Bearer"));

        // Act + Assert
        Assert.Equal(userId, authenticated.GetProducerUserId());
        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).GetProducerUserId());
    }

    [Fact]
    public void Given_CorsConfiguration_When_ReadingThePolicy_Should_AllowTheOriginsAndExposeRetryAfter()
    {
        // Arrange
        using var services = BuildServices(ProducerSessionTestData.JWT_CONFIGURATION.SigningKey);

        // Act
        var policy = services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(CorsExtensions.WEB_POLICY_NAME);

        // Assert
        Assert.NotNull(policy);
        Assert.Equal(["https://produtores.ntix.com.br"], policy.Origins);
        Assert.Contains("Retry-After", policy.ExposedHeaders);
        Assert.True(policy.AllowAnyHeader);
        Assert.False(policy.SupportsCredentials);
        Assert.Contains("PUT", policy.Methods);
    }
}
