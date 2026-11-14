namespace WebApi.Controllers.Payloads;

/// <param name="IntegrationIds">Integrações em ordem de prioridade (primeiro = 1). Lista vazia remove o método.</param>
public sealed record ReplacePaymentRouteV1Request(List<string?>? IntegrationIds);
