using Infra.Resend.Base.Configuration;
using Infra.Resend.Base.EmailSender.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Resend.Base.EmailSender;

public static class ServiceCollectionExtensions
{
    private const string BASE_ADDRESS = "https://api.resend.com/";

    private static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddResendConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var resendConfiguration = configuration.GetRequiredSection("ResendConfiguration").Get<ResendConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(resendConfiguration.ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(resendConfiguration.From);

        services.AddSingleton(resendConfiguration);
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri(BASE_ADDRESS);
            client.Timeout = TIMEOUT;
        });

        return services;
    }
}
