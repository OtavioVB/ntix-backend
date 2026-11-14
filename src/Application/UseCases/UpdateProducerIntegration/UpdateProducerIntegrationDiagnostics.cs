using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.UpdateProducerIntegration;

public sealed class UpdateProducerIntegrationDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_UPDATED = "updated";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";
    public const string OUTCOME_NOT_FOUND = "not_found";
    public const string OUTCOME_NAME_ALREADY_EXISTS = "name_already_exists";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public UpdateProducerIntegrationDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.integrations.update", "Alterações de integração de pagamento pelo produtor", OUTCOME_UPDATED, OUTCOME_PRODUCER_NOT_FOUND, OUTCOME_NOT_FOUND, OUTCOME_NAME_ALREADY_EXISTS)
    {
    }
}
