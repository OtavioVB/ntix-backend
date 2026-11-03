namespace WebApi.Cors;

public sealed record CorsConfiguration(string[] AllowedOrigins);

public static class CorsExtensions
{
    public const string WEB_POLICY_NAME = "ntix-web";

    /// <summary>Libera o front (ntix-web), que chama a API de outra origem, para as origens configuradas.</summary>
    public static IServiceCollection AddCorsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var corsConfiguration = configuration.GetRequiredSection("CorsConfiguration").Get<CorsConfiguration>()!;

        services.AddCors(options => options.AddPolicy(WEB_POLICY_NAME, policy => policy
            .WithOrigins(corsConfiguration.AllowedOrigins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete)
            .AllowAnyHeader()));

        return services;
    }
}
