using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.UpdateTicketBatch;

public sealed class UpdateTicketBatchDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_UPDATED = "updated";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";
    public const string OUTCOME_BATCH_NOT_FOUND = "batch_not_found";
    public const string OUTCOME_EVENT_ALREADY_STARTED = "event_already_started";
    public const string OUTCOME_NAME_ALREADY_EXISTS = "name_already_exists";
    public const string OUTCOME_QUANTITY_BELOW_SOLD = "quantity_below_sold";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public UpdateTicketBatchDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.batches.update", "Alteração de lotes pelo produtor", OUTCOME_UPDATED, OUTCOME_EVENT_NOT_FOUND, OUTCOME_BATCH_NOT_FOUND, OUTCOME_EVENT_ALREADY_STARTED, OUTCOME_NAME_ALREADY_EXISTS, OUTCOME_QUANTITY_BELOW_SOLD)
    {
    }
}
