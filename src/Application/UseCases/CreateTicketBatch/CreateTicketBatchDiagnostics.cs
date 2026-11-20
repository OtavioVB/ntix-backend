using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateTicketBatch;

public sealed class CreateTicketBatchDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";
    public const string OUTCOME_EVENT_ALREADY_STARTED = "event_already_started";
    public const string OUTCOME_NAME_ALREADY_EXISTS = "name_already_exists";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public CreateTicketBatchDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.batches.create", "Criação de lotes pelo produtor", OUTCOME_CREATED, OUTCOME_EVENT_NOT_FOUND, OUTCOME_EVENT_ALREADY_STARTED, OUTCOME_NAME_ALREADY_EXISTS)
    {
    }
}
