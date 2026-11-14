using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetPaymentProviders;

public sealed class GetPaymentProvidersDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_SERVED = "served";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    public GetPaymentProvidersDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.providers", "Consultas do catálogo de fornecedores de pagamento", OUTCOME_SERVED)
    {
    }
}
