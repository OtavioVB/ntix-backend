using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateProducerIntegration;

public sealed class CreateProducerIntegrationDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_PRODUCER_NOT_FOUND = "producer_not_found";
    public const string OUTCOME_NAME_ALREADY_EXISTS = "name_already_exists";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public CreateProducerIntegrationDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.integrations.creation", "Conexões de fornecedor de pagamento pelo produtor", OUTCOME_CREATED, OUTCOME_PRODUCER_NOT_FOUND, OUTCOME_NAME_ALREADY_EXISTS)
    {
    }
}
