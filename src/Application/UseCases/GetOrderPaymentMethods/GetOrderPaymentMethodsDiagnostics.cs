using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetOrderPaymentMethods;

public sealed class GetOrderPaymentMethodsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_ORDER_NOT_FOUND = "order_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetOrderPaymentMethodsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.methods", "Consultas dos métodos de pagamento do pedido", OUTCOME_SERVED, OUTCOME_ORDER_NOT_FOUND)
    {
    }
}
