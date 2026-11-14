using Application.UseCases.Base.Payments.Outputs;

namespace Application.UseCases.GetPaymentRoutes.Outputs;

/// <param name="Routes">Os três métodos, sempre presentes, com as integrações em ordem de prioridade.</param>
public sealed record GetPaymentRoutesUseCaseOutput(IReadOnlyDictionary<string, IReadOnlyList<PaymentRouteItemOutput>> Routes);
