using Infra.PostgreSql.Base.Configuration;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.PostgreSql.Base.UnitOfWork;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSqlConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var postgreSqlConfiguration = configuration.GetRequiredSection("PostgreSqlConfiguration").Get<PostgreSqlConfiguration>()!;

        services.AddSingleton(postgreSqlConfiguration);
        services.AddScoped<PostgreSqlUnitOfWork>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<PostgreSqlUnitOfWork>());

        return services;
    }
}
