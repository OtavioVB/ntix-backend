using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.PostgreSql.Repositories.Payments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentRepositoriesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPaymentProviderRepository, PaymentProviderRepository>();
        services.AddScoped<IProducerIntegrationRepository, ProducerIntegrationRepository>();
        services.AddScoped<IPaymentRouteRepository, PaymentRouteRepository>();

        return services;
    }
}
