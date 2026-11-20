using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateCustomerSession;

public sealed class CreateCustomerSessionDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_INVALID_CREDENTIALS = "invalid_credentials";
    public const string OUTCOME_RATE_LIMITED = "rate_limited";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _rateLimited;

    public CreateCustomerSessionDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.customers.sessions.create", "Logins de cliente", OUTCOME_CREATED, OUTCOME_INVALID_CREDENTIALS, OUTCOME_RATE_LIMITED)
    {
        _rateLimited = Meter.CreateCounter<long>(
            name: "ntix.customers.sessions.login.rate_limited",
            unit: "{request}",
            description: "Logins recusados pelo limite de tentativas por login (REQ12, RN9), para a operação acompanhar travas de conta.");

        _rateLimited.Add(0);
    }

    public void RateLimited()
        => _rateLimited.Add(1);
}
