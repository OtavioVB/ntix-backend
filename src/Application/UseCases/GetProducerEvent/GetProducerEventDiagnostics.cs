using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetProducerEvent;

public sealed class GetProducerEventDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetProducerEventDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.get", "Consulta de um evento pelo produtor", OUTCOME_SERVED, OUTCOME_PRODUCER_NOT_FOUND, OUTCOME_EVENT_NOT_FOUND)
    {
    }
}
