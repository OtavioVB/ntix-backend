using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.RevokeCustomerSession;

public sealed class RevokeCustomerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_REVOKED = "revoked";
    public const string OUTCOME_NOT_FOUND = "not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public RevokeCustomerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.sessions.revoke", "Logouts de cliente", OUTCOME_REVOKED, OUTCOME_NOT_FOUND)
    {
    }
}
