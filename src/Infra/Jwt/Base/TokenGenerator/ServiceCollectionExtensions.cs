using Infra.Jwt.Base.Configuration;
using Infra.Jwt.Base.TokenGenerator.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Infra.Jwt.Base.TokenGenerator;

public static class ServiceCollectionExtensions
{
    private const int SECRET_KEY_MIN_BYTES = 32;

    public static IServiceCollection AddJwtConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfiguration = configuration.GetRequiredSection("JwtConfiguration").Get<JwtConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(jwtConfiguration.SecretKey);

        if (Encoding.UTF8.GetByteCount(jwtConfiguration.SecretKey) < SECRET_KEY_MIN_BYTES)
            throw new ArgumentException($"The JWT secret key must have at least {SECRET_KEY_MIN_BYTES} bytes.");

        services.AddSingleton(jwtConfiguration);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
