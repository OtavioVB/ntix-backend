using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.RefreshCustomerSession;

public sealed class RefreshCustomerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_REFRESHED = "refreshed";
    public const string OUTCOME_INVALID_TOKEN = "invalid_token";
    public const string OUTCOME_REUSE_DETECTED = "reuse_detected";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _reuse;

    public RefreshCustomerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.sessions.refresh", "Renovações de sessão de cliente", OUTCOME_REFRESHED, OUTCOME_INVALID_TOKEN, OUTCOME_REUSE_DETECTED)
    {
        _reuse = Meter.CreateCounter<long>(
            name: "ntix.customers.sessions.refresh.reuse",
            unit: "{request}",
            description: "Refresh tokens já substituídos reapresentados (REQ12, RN5): indício de roubo, que revoga a família inteira.");

        _reuse.Add(0);
    }

    public void ReuseDetected()
        => _reuse.Add(1);
}
