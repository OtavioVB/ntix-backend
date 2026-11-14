namespace Application.UseCases.Base.Payments.Outputs;

/// <summary>Uma posição da rota de um método: a integração e a prioridade dela (1 = principal).</summary>
public sealed record PaymentRouteItemOutput(int Priority, Guid IntegrationId, string Name, string Status);
