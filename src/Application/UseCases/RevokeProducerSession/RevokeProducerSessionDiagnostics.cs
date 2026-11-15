using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.RevokeProducerSession;

public sealed class RevokeProducerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Producers";

    public const string OUTCOME_REVOKED = "revoked";
    public const string OUTCOME_NOT_FOUND = "not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public RevokeProducerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.producers.sessions.revocation", "Logouts de usuário de produtora", OUTCOME_REVOKED, OUTCOME_NOT_FOUND)
    {
    }
}
