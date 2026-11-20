using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetCustomerOrder;

public sealed class GetCustomerOrderDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Orders";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_ORDER_NOT_FOUND = "order_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetCustomerOrderDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.orders.get", "Consultas de pedido pelo comprador", OUTCOME_SERVED, OUTCOME_ORDER_NOT_FOUND)
    {
    }
}
