using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.PostgreSql.Repositories.Orders;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderRepositoriesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();

        return services;
    }
}
