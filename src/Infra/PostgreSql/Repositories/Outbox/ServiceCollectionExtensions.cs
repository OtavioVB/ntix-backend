using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.PostgreSql.Repositories.Outbox;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxRepositoryConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        return services;
    }
}
