using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetProducerEvents;

public sealed class GetProducerEventsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetProducerEventsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.list", "Lista de eventos do produtor", OUTCOME_SERVED, OUTCOME_PRODUCER_NOT_FOUND)
    {
    }
}
