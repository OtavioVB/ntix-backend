using Infra.SqlServer.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.SqlServer.Repositories.Producers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProducerRepositoryConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProducerRepository, ProducerRepository>();

        return services;
    }
}
