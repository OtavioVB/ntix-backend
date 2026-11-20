using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateOrderPayment;

public sealed class CreateOrderPaymentDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_REUSED = "reused";
    public const string OUTCOME_UNAVAILABLE = "unavailable";
    public const string OUTCOME_PROVIDERS_FAILED = "providers_failed";
    public const string OUTCOME_ORDER_PAID = "order_paid";
    public const string OUTCOME_ORDER_EXPIRED = "order_expired";
    public const string OUTCOME_WINDOW_TOO_SHORT = "window_too_short";
    public const string OUTCOME_ORDER_NOT_FOUND = "order_not_found";

    /// <summary>Desfecho de uma chamada ao fornecedor que gerou o Pix.</summary>
    public const string PROVIDER_CALL_CREATED = "created";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _providerCalls;
    private readonly Histogram<double> _providerDuration;
    private readonly Counter<long> _fallbackUsed;

    public CreateOrderPaymentDiagnostics(IMeterFactory meterFactory)
        : base(
            meterFactory, NAME, "ntix.payments.pix.create", "Geração de Pix para pedidos",
            OUTCOME_CREATED, OUTCOME_REUSED, OUTCOME_UNAVAILABLE, OUTCOME_PROVIDERS_FAILED, OUTCOME_ORDER_PAID, OUTCOME_ORDER_EXPIRED, OUTCOME_WINDOW_TOO_SHORT, OUTCOME_ORDER_NOT_FOUND)
    {
        _providerCalls = Meter.CreateCounter<long>(
            name: "ntix.payments.provider.calls",
            unit: "{call}",
            description: "Chamadas ao fornecedor de pagamento, por fornecedor e desfecho (created ou o motivo da falha).");

        _providerDuration = Meter.CreateHistogram<double>(
            name: "ntix.payments.provider.duration",
            unit: "s",
            description: "Duração das chamadas ao fornecedor de pagamento, por fornecedor.");

        _fallbackUsed = Meter.CreateCounter<long>(
            name: "ntix.payments.fallback.used",
            unit: "{payment}",
            description: "Pagamentos gerados por uma integração que não era a de prioridade 1.");

        _fallbackUsed.Add(0);
    }

    public void ProviderCalled(string provider, string outcome, TimeSpan duration)
    {
        _providerCalls.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("outcome", outcome));
        _providerDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("provider", provider));
    }

    public void FallbackUsed()
        => _fallbackUsed.Add(1);
}
