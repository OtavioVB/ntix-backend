using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.UpdateEvent;

public sealed class UpdateEventDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_UPDATED = "updated";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";
    public const string OUTCOME_ALREADY_STARTED = "already_started";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public UpdateEventDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.update", "Alteração de eventos pelo produtor", OUTCOME_UPDATED, OUTCOME_PRODUCER_NOT_FOUND, OUTCOME_EVENT_NOT_FOUND, OUTCOME_ALREADY_STARTED)
    {
    }
}
