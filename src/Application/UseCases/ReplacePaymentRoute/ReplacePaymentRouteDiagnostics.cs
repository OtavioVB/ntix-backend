using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.ReplacePaymentRoute;

public sealed class ReplacePaymentRouteDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_REPLACED = "replaced";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public ReplacePaymentRouteDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.routes.replacement", "Substituições da rota de um método de pagamento", OUTCOME_REPLACED, OUTCOME_PRODUCER_NOT_FOUND)
    {
    }
}
