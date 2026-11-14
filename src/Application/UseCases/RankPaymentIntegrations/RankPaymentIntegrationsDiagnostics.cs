using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.RankPaymentIntegrations;

public sealed class RankPaymentIntegrationsDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_RANKED = "ranked";
    public const string OUTCOME_UNAVAILABLE = "unavailable";

    /// <summary>Tag de fornecedor quando o método não tem nenhuma rota.</summary>
    public const string NO_PROVIDER = "none";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _empty;

    public RankPaymentIntegrationsDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.payments.ranking", "Ranqueamentos de integração de pagamento", OUTCOME_RANKED, OUTCOME_UNAVAILABLE)
    {
        _empty = Meter.CreateCounter<long>(
            name: "ntix.payments.ranking.empty",
            unit: "{ranking}",
            description: "Ranqueamentos sem integração disponível: o método some do checkout do produtor.");
    }

    /// <param name="provider">Fornecedor da integração de prioridade 1, ou <see cref="NO_PROVIDER"/> sem rota.</param>
    public void Empty(string paymentMethod, string provider)
        => _empty.Add(1, new KeyValuePair<string, object?>("method", paymentMethod), new KeyValuePair<string, object?>("provider", provider));
}
