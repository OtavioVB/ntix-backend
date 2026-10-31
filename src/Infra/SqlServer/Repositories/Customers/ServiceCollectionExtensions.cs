using Infra.SqlServer.Repositories.Customers.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.SqlServer.Repositories.Customers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerRepositoryConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();

        return services;
    }
}
