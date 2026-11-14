using Application.UseCases.Base.Payments.Outputs;

namespace Application.UseCases.GetProducerIntegrations.Outputs;

public sealed record GetProducerIntegrationsUseCaseOutput(IReadOnlyList<ProducerIntegrationOutput> Integrations);
