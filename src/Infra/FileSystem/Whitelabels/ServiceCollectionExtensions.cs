using Infra.FileSystem.Base.Configuration;
using Infra.FileSystem.Whitelabels.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.FileSystem.Whitelabels;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWhitelabelReaderConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var whitelabelConfiguration = configuration.GetRequiredSection("WhitelabelConfiguration").Get<WhitelabelConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(whitelabelConfiguration.DirectoryPath);

        services.AddSingleton(whitelabelConfiguration);
        services.AddSingleton<IWhitelabelReader, WhitelabelReader>();

        return services;
    }
}
