using Infra.SqlServer.Base.Configuration;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.SqlServer.Base.UnitOfWork;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var sqlServerConfiguration = configuration.GetRequiredSection("SqlServerConfiguration").Get<SqlServerConfiguration>()!;

        services.AddSingleton(sqlServerConfiguration);
        services.AddScoped<SqlServerUnitOfWork>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<SqlServerUnitOfWork>());

        return services;
    }
}
