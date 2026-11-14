using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Application.UseCases.GetProducerWhitelabel;
using Application.UseCases.GetProducerWhitelabel.Inputs;
using Application.UseCases.GetProducerWhitelabel.Outputs;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateProducerIntegration;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.GetPaymentProviders;
using Application.UseCases.GetPaymentProviders.Inputs;
using Application.UseCases.GetPaymentProviders.Outputs;
using Application.UseCases.GetPaymentRoutes;
using Application.UseCases.GetPaymentRoutes.Inputs;
using Application.UseCases.GetPaymentRoutes.Outputs;
using Application.UseCases.GetProducerIntegrations;
using Application.UseCases.GetProducerIntegrations.Inputs;
using Application.UseCases.GetProducerIntegrations.Outputs;
using Application.UseCases.RankPaymentIntegrations;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Application.UseCases.ReplacePaymentRoute;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Application.UseCases.UpdateProducerIntegration;
using Application.UseCases.UpdateProducerIntegration.Inputs;
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

        services.AddSingleton<GetPaymentProvidersDiagnostics>();
        services.AddScoped<IUseCase<GetPaymentProvidersUseCaseInput, GetPaymentProvidersUseCaseOutput>, GetPaymentProvidersUseCase>();
        services.AddSingleton<CreateProducerIntegrationDiagnostics>();
        services.AddScoped<IUseCase<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>, CreateProducerIntegrationUseCase>();
        services.AddSingleton<GetProducerIntegrationsDiagnostics>();
        services.AddScoped<IUseCase<GetProducerIntegrationsUseCaseInput, GetProducerIntegrationsUseCaseOutput>, GetProducerIntegrationsUseCase>();
        services.AddSingleton<UpdateProducerIntegrationDiagnostics>();
        services.AddScoped<IUseCase<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>, UpdateProducerIntegrationUseCase>();
        services.AddSingleton<GetPaymentRoutesDiagnostics>();
        services.AddScoped<IUseCase<GetPaymentRoutesUseCaseInput, GetPaymentRoutesUseCaseOutput>, GetPaymentRoutesUseCase>();
        services.AddSingleton<ReplacePaymentRouteDiagnostics>();
        services.AddScoped<IUseCase<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>, ReplacePaymentRouteUseCase>();
        services.AddSingleton<RankPaymentIntegrationsDiagnostics>();
        services.AddScoped<IUseCase<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>, RankPaymentIntegrationsUseCase>();

        return services;
    }
}
