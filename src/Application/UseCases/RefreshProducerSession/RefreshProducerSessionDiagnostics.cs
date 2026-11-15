using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.RefreshProducerSession;

public sealed class RefreshProducerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Producers";

    public const string OUTCOME_REFRESHED = "refreshed";
    public const string OUTCOME_INVALID_TOKEN = "invalid_token";
    public const string OUTCOME_REUSE_DETECTED = "reuse_detected";
    public const string OUTCOME_BLOCKED = "blocked";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _reuse;

    public RefreshProducerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.producers.sessions.refresh", "Renovações de sessão de usuário de produtora", OUTCOME_REFRESHED, OUTCOME_INVALID_TOKEN, OUTCOME_REUSE_DETECTED, OUTCOME_BLOCKED)
    {
        _reuse = Meter.CreateCounter<long>(
            name: "ntix.producers.sessions.refresh.reuse",
            unit: "{request}",
            description: "Refresh tokens já substituídos reapresentados (RN7): indício de roubo, que revoga a família inteira.");

        _reuse.Add(0);
    }

    public void ReuseDetected()
        => _reuse.Add(1);
}
