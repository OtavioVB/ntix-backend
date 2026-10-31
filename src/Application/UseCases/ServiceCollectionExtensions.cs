using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Application.UseCases;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUseCasesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMetrics();
        services.AddSingleton<CreateCustomerDiagnostics>();
        services.AddScoped<IUseCase<CreateCustomerUseCaseInput>, CreateCustomerUseCase>();

        return services;
    }
}
