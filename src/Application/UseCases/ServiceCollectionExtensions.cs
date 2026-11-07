using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Application.UseCases.GetProducerWhitelabel;
using Application.UseCases.GetProducerWhitelabel.Inputs;
using Application.UseCases.GetProducerWhitelabel.Outputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Application.UseCases;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUseCasesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var passwordConfiguration = configuration.GetRequiredSection("PasswordConfiguration").Get<PasswordConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordConfiguration.PrivateKey);

        services.AddSingleton(passwordConfiguration);
        services.AddMetrics();
        services.AddSingleton<CreateCustomerDiagnostics>();
        services.AddScoped<IUseCase<CreateCustomerUseCaseInput>, CreateCustomerUseCase>();
        services.AddSingleton<CreateProducerDiagnostics>();
        services.AddScoped<IUseCase<CreateProducerUseCaseInput>, CreateProducerUseCase>();
        services.AddSingleton<GetProducerWhitelabelDiagnostics>();
        services.AddScoped<IUseCase<GetProducerWhitelabelUseCaseInput, GetProducerWhitelabelUseCaseOutput>, GetProducerWhitelabelUseCase>();

        return services;
    }
}
