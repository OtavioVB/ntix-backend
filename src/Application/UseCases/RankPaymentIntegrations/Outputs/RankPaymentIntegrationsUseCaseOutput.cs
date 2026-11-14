namespace Application.UseCases.RankPaymentIntegrations.Outputs;

/// <param name="Integrations">Em ordem: a primeira é a principal e as demais são fallback (REQ7).</param>
public sealed record RankPaymentIntegrationsUseCaseOutput(string PaymentMethod, IReadOnlyList<RankedIntegrationOutput> Integrations);

/// <param name="Priority">Prioridade na rota, que pode ter buracos aqui: as integrações descartadas não aparecem.</param>
public sealed record RankedIntegrationOutput(int Priority, Guid IntegrationId, string ProviderCode);
