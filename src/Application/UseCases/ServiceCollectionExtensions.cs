using Application.UseCases.AuthenticateCustomer;
using Application.UseCases.AuthenticateCustomer.Inputs;
using Application.UseCases.AuthenticateCustomer.Outputs;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Application.UseCases;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUseCasesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var passwordConfiguration = configuration.GetRequiredSection("PasswordConfiguration").Get<PasswordConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordConfiguration.PrivateKey);

        var emailVerificationConfiguration = configuration.GetRequiredSection("EmailVerificationConfiguration").Get<EmailVerificationConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(emailVerificationConfiguration.LinkBaseUrl);

        services.AddSingleton(passwordConfiguration);
        services.AddSingleton(emailVerificationConfiguration);
        services.AddMetrics();
        services.AddSingleton<CreateCustomerDiagnostics>();
        services.AddScoped<IUseCase<CreateCustomerUseCaseInput>, CreateCustomerUseCase>();
        services.AddSingleton<AuthenticateCustomerDiagnostics>();
        services.AddScoped<IUseCase<AuthenticateCustomerUseCaseInput, AuthenticateCustomerUseCaseOutput>, AuthenticateCustomerUseCase>();

        services.AddSingleton<CreateProducerDiagnostics>();
        services.AddScoped<IUseCase<CreateProducerUseCaseInput>, CreateProducerUseCase>();

        return services;
    }
}
