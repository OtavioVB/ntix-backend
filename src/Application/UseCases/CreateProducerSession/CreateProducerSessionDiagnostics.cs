using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateProducerSession;

public sealed class CreateProducerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Producers";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_INVALID_CREDENTIALS = "invalid_credentials";
    public const string OUTCOME_BLOCKED = "blocked";
    public const string OUTCOME_RATE_LIMITED = "rate_limited";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _rateLimited;

    public CreateProducerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.producers.sessions.creation", "Logins de usuário de produtora", OUTCOME_CREATED, OUTCOME_INVALID_CREDENTIALS, OUTCOME_BLOCKED, OUTCOME_RATE_LIMITED)
    {
        _rateLimited = Meter.CreateCounter<long>(
            name: "ntix.producers.sessions.login.rate_limited",
            unit: "{request}",
            description: "Logins recusados pelo limite de tentativas por e-mail (RN12), para a operação acompanhar travas de conta.");

        _rateLimited.Add(0);
    }

    public void RateLimited()
        => _rateLimited.Add(1);
}
