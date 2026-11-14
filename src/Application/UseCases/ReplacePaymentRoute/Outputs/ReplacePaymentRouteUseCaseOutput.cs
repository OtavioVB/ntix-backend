using Application.UseCases.Base.Payments.Outputs;

namespace Application.UseCases.ReplacePaymentRoute.Outputs;

/// <param name="Integrations">A nova lista do método, em ordem de prioridade.</param>
public sealed record ReplacePaymentRouteUseCaseOutput(IReadOnlyList<PaymentRouteItemOutput> Integrations);
