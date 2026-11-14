using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetProducerIntegrations;

public sealed class GetProducerIntegrationsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetProducerIntegrationsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.integrations.query", "Consultas das integrações de pagamento do produtor", OUTCOME_SERVED, OUTCOME_PRODUCER_NOT_FOUND)
    {
    }
}
