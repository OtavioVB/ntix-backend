using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetEventDailySales;

public sealed class GetEventDailySalesDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetEventDailySalesDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.sales.daily", "Vendas por dia do evento", OUTCOME_SERVED, OUTCOME_EVENT_NOT_FOUND)
    {
    }
}
