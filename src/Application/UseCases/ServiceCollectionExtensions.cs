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
using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession;
using Application.UseCases.RevokeProducerSession.Inputs;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.CreateEvent;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.GetProducerEvent;
using Application.UseCases.GetProducerEvent.Inputs;
using Application.UseCases.GetProducerEvents;
using Application.UseCases.GetProducerEvents.Inputs;
using Application.UseCases.GetProducerEvents.Outputs;
using Application.UseCases.UpdateEvent;
using Application.UseCases.UpdateEvent.Inputs;
using Application.UseCases.CreateTicketBatch;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetTicketBatches;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Application.UseCases.PublishEvent;
using Application.UseCases.PublishEvent.Inputs;
using Application.UseCases.UpdateTicketBatch;
using Application.UseCases.UpdateTicketBatch.Inputs;
using Application.UseCases.GetPublicEvent;
using Application.UseCases.GetPublicEvent.Inputs;
using Application.UseCases.SearchPublicEvents;
using Application.UseCases.SearchPublicEvents.Inputs;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.CreateCustomerSession;
using Application.UseCases.CreateCustomerSession.Inputs;
using Application.UseCases.GetCurrentCustomer;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Application.UseCases.RefreshCustomerSession;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Application.UseCases.RevokeCustomerSession;
using Application.UseCases.RevokeCustomerSession.Inputs;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrder;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.GetCustomerOrder;
using Application.UseCases.GetCustomerOrder.Inputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.UseCases;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUseCasesConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var passwordConfiguration = configuration.GetRequiredSection("PasswordConfiguration").Get<PasswordConfiguration>()!;

        ArgumentException.ThrowIfNullOrWhiteSpace(passwordConfiguration.PrivateKey);

        services.AddSingleton(passwordConfiguration);
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);

        AddProducerSessions(services, configuration);
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

        services.AddSingleton<CreateEventDiagnostics>();
        services.AddScoped<IUseCase<CreateEventUseCaseInput, ProducerEventOutput>, CreateEventUseCase>();
        services.AddSingleton<GetProducerEventsDiagnostics>();
        services.AddScoped<IUseCase<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput>, GetProducerEventsUseCase>();
        services.AddSingleton<GetProducerEventDiagnostics>();
        services.AddScoped<IUseCase<GetProducerEventUseCaseInput, ProducerEventOutput>, GetProducerEventUseCase>();
        services.AddSingleton<UpdateEventDiagnostics>();
        services.AddScoped<IUseCase<UpdateEventUseCaseInput, ProducerEventOutput>, UpdateEventUseCase>();
        services.AddSingleton<CreateTicketBatchDiagnostics>();
        services.AddScoped<IUseCase<CreateTicketBatchUseCaseInput, TicketBatchOutput>, CreateTicketBatchUseCase>();
        services.AddSingleton<GetTicketBatchesDiagnostics>();
        services.AddScoped<IUseCase<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>, GetTicketBatchesUseCase>();
        services.AddSingleton<UpdateTicketBatchDiagnostics>();
        services.AddScoped<IUseCase<UpdateTicketBatchUseCaseInput, TicketBatchOutput>, UpdateTicketBatchUseCase>();
        services.AddSingleton<PublishEventDiagnostics>();
        services.AddScoped<IUseCase<PublishEventUseCaseInput, ProducerEventOutput>, PublishEventUseCase>();

        services.AddSingleton(configuration.GetSection("OrderConfiguration").Get<OrderConfiguration>() ?? new OrderConfiguration());
        services.AddSingleton<SearchPublicEventsDiagnostics>();
        services.AddScoped<IUseCase<SearchPublicEventsUseCaseInput, PublicEventsPageOutput>, SearchPublicEventsUseCase>();
        services.AddSingleton<GetPublicEventDiagnostics>();
        services.AddScoped<IUseCase<GetPublicEventUseCaseInput, PublicEventOutput>, GetPublicEventUseCase>();

        services.AddSingleton<CreateOrderDiagnostics>();
        services.AddScoped<IUseCase<CreateOrderUseCaseInput, OrderOutput>, CreateOrderUseCase>();
        services.AddSingleton<GetCustomerOrderDiagnostics>();
        services.AddScoped<IUseCase<GetCustomerOrderUseCaseInput, OrderOutput>, GetCustomerOrderUseCase>();

        return services;
    }

    // Login, renovação e logout do usuário do produtor (REQ5). A configuração é conferida aqui, para a API não subir sem ela.
    private static void AddProducerSessions(IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfiguration = configuration.GetRequiredSection("JwtConfiguration").Get<JwtConfiguration>()!;
        var loginAttemptConfiguration = configuration.GetSection("LoginAttemptConfiguration").Get<LoginAttemptConfiguration>() ?? new LoginAttemptConfiguration();

        jwtConfiguration.GetValidatedSigningKey();

        services.AddSingleton(jwtConfiguration);
        services.AddSingleton(loginAttemptConfiguration);
        services.AddSingleton<ProducerAccessTokenIssuer>();
        services.AddSingleton<LoginAttemptLimiter>();

        services.AddSingleton<CreateProducerSessionDiagnostics>();
        services.AddScoped<IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput>, CreateProducerSessionUseCase>();
        services.AddSingleton<RefreshProducerSessionDiagnostics>();
        services.AddScoped<IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput>, RefreshProducerSessionUseCase>();
        services.AddSingleton<RevokeProducerSessionDiagnostics>();
        services.AddScoped<IUseCase<RevokeProducerSessionUseCaseInput>, RevokeProducerSessionUseCase>();

        AddCustomerSessions(services, configuration);
    }

    // Login, renovação, logout e meus dados do cliente (REQ12), com a mesma JwtConfiguration e o mesmo limitador do REQ5.
    private static void AddCustomerSessions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection("CustomerSessionConfiguration").Get<CustomerSessionConfiguration>() ?? new CustomerSessionConfiguration());
        services.AddSingleton<CustomerAccessTokenIssuer>();

        services.AddSingleton<CreateCustomerSessionDiagnostics>();
        services.AddScoped<IUseCase<CreateCustomerSessionUseCaseInput, CustomerSessionOutput>, CreateCustomerSessionUseCase>();
        services.AddSingleton<RefreshCustomerSessionDiagnostics>();
        services.AddScoped<IUseCase<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput>, RefreshCustomerSessionUseCase>();
        services.AddSingleton<RevokeCustomerSessionDiagnostics>();
        services.AddScoped<IUseCase<RevokeCustomerSessionUseCaseInput>, RevokeCustomerSessionUseCase>();
        services.AddSingleton<GetCurrentCustomerDiagnostics>();
        services.AddScoped<IUseCase<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput>, GetCurrentCustomerUseCase>();
    }
}
