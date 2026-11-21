using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetCustomerTickets;

public sealed class GetCustomerTicketsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_CUSTOMER_NOT_FOUND = "customer_not_found";
    public const string OUTCOME_CANCELED = "canceled";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetCustomerTicketsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.tickets.list", "Lista de ingressos do comprador", OUTCOME_SERVED, OUTCOME_CUSTOMER_NOT_FOUND, OUTCOME_CANCELED)
    {
    }
}
