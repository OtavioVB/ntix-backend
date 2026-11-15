using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace WebApi.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>Política dos endpoints do painel do produtor: token válido e slug da rota igual ao do token.</summary>
    public const string PRODUCER_USER_POLICY = "ProducerUser";

    private static readonly TimeSpan CLOCK_SKEW = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validação do access token emitido pelo login (REQ5). Usa a mesma <see cref="JwtConfiguration"/> da emissão; a API não
    /// sobe sem a chave de assinatura ou com menos de 32 bytes.
    /// </summary>
    public static IServiceCollection AddProducerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfiguration = configuration.GetRequiredSection("JwtConfiguration").Get<JwtConfiguration>()!;
        var signingKey = jwtConfiguration.GetValidatedSigningKey();

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
}
