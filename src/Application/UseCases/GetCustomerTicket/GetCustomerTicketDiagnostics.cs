using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetCustomerTicket;

public sealed class GetCustomerTicketDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_NOT_FOUND = "not_found";
    public const string OUTCOME_CANCELED = "canceled";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetCustomerTicketDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.tickets.get", "Detalhe do ingresso do comprador", OUTCOME_SERVED, OUTCOME_NOT_FOUND, OUTCOME_CANCELED)
    {
    }
}
