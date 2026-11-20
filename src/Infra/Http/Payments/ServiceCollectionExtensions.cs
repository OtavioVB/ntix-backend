using Infra.Http.Payments.AbacatePay;
using Infra.Http.Payments.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Http.Payments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPixChargeClientsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var abacatePayConfiguration = configuration.GetSection("AbacatePayConfiguration").Get<AbacatePayConfiguration>() ?? new AbacatePayConfiguration();

        services.AddSingleton(abacatePayConfiguration);

        // O HttpClient não tem timeout próprio: cada tentativa tem o seu, por CancellationTokenSource, para distinguir tempo esgotado de cancelamento.
        services.AddHttpClient<AbacatePayClient>(client =>
        {
            client.BaseAddress = new Uri(abacatePayConfiguration.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddTransient<IPixChargeClient>(provider => provider.GetRequiredService<AbacatePayClient>());

        return services;
    }
}
