using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace WebApi.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>Política dos endpoints do painel do produtor: token válido e slug da rota igual ao do token.</summary>
    public const string PRODUCER_USER_POLICY = "ProducerUser";

    public const int SIGNING_KEY_MIN_SIZE_IN_BYTES = 32;

    private static readonly TimeSpan CLOCK_SKEW = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddProducerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfiguration = configuration.GetRequiredSection("JwtConfiguration").Get<JwtConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(jwtConfiguration.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtConfiguration.Audience);

        var signingKey = DecodeSigningKey(jwtConfiguration.SigningKey);

        services.AddSingleton(jwtConfiguration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Mantém "sub" e as claims da Ntix com o nome do token, sem o mapeamento para os tipos longos do .NET.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwtConfiguration.Issuer,
                    ValidAudience = jwtConfiguration.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = CLOCK_SKEW,
                    NameClaimType = ProducerClaims.SUBJECT,
                };
            });

        services.AddSingleton<IAuthorizationHandler, ProducerSlugAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(PRODUCER_USER_POLICY, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ProducerClaims.SUBJECT)
                .RequireClaim(ProducerClaims.PRODUCER_SLUG)
                .AddRequirements(new ProducerSlugRequirement()));

        return services;
    }

    /// <summary>Usuário do produtor que fez a requisição (RN10 do REQ5): é ele o <c>ExecutionUser</c>, e não o header.</summary>
    public static string? GetProducerUserId(this ClaimsPrincipal user)
        => user.FindFirst(ProducerClaims.SUBJECT)?.Value;

    private static byte[] DecodeSigningKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("JwtConfiguration:SigningKey is required.");

        byte[] key;

        try
        {
            key = Convert.FromBase64String(signingKey);
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
