using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetPaymentRoutes;

public sealed class GetPaymentRoutesDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetPaymentRoutesDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.routes.query", "Consultas das rotas de pagamento do produtor", OUTCOME_SERVED, OUTCOME_PRODUCER_NOT_FOUND)
    {
    }
}
