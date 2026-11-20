using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.PublishEvent;

public sealed class PublishEventDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Events";

    public const string OUTCOME_PUBLISHED = "published";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";
    public const string OUTCOME_NOT_DRAFT = "not_draft";
    public const string OUTCOME_EVENT_ALREADY_STARTED = "event_already_started";
    public const string OUTCOME_WITHOUT_BATCH = "without_batch";
    public const string OUTCOME_WITHOUT_PAYMENT_METHOD = "without_payment_method";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public PublishEventDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.events.publish", "Publicação de eventos pelo produtor", OUTCOME_PUBLISHED, OUTCOME_EVENT_NOT_FOUND, OUTCOME_NOT_DRAFT, OUTCOME_EVENT_ALREADY_STARTED, OUTCOME_WITHOUT_BATCH, OUTCOME_WITHOUT_PAYMENT_METHOD)
    {
    }
}
