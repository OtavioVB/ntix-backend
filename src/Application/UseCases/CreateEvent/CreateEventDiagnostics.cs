using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateEvent;

public sealed class CreateEventDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public CreateEventDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.create", "Criação de eventos pelo produtor", OUTCOME_CREATED, OUTCOME_PRODUCER_NOT_FOUND)
    {
    }
}
