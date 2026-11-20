using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.SearchPublicEvents;

public sealed class SearchPublicEventsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_SERVED = "served";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public SearchPublicEventsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.search", "Consultas da vitrine de eventos", OUTCOME_SERVED)
    {
    }
}
