using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetCurrentCustomer;

public sealed class GetCurrentCustomerDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_CUSTOMER_NOT_FOUND = "customer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetCurrentCustomerDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.me", "Consultas dos dados do cliente", OUTCOME_SERVED, OUTCOME_CUSTOMER_NOT_FOUND)
    {
    }
}
