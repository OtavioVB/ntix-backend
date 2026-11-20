using Infra.PostgreSql.Repositories.Events.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.PostgreSql.Repositories.Events;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventRepositoriesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IEventRepository, EventRepository>();

        return services;
    }
}
