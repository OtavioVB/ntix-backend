using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.ConfirmPaymentFromWebhook;

public sealed class ConfirmPaymentFromWebhookDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Payments";

    public const string OUTCOME_CONFIRMED = "confirmed";
    public const string OUTCOME_DUPLICATE = "duplicate";
    public const string OUTCOME_ALREADY_PAID = "already_paid";
    public const string OUTCOME_IGNORED = "ignored";
    public const string OUTCOME_NOT_PAID_AT_SOURCE = "not_paid_at_source";
    public const string OUTCOME_AMOUNT_MISMATCH = "amount_mismatch";
    public const string OUTCOME_INVALID_SIGNATURE = "invalid_signature";
    public const string OUTCOME_INVALID_SECRET = "invalid_secret";
    public const string OUTCOME_PAYMENT_NOT_FOUND = "payment_not_found";
    public const string OUTCOME_SOURCE_UNAVAILABLE = "source_unavailable";
    public const string OUTCOME_INVALID_BODY = "invalid_body";
    public const string OUTCOME_CANCELED = "canceled";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _paidAfterExpiration;
    private readonly Histogram<double> _confirmationDelay;

    public ConfirmPaymentFromWebhookDiagnostics(IMeterFactory meterFactory)
        : base(
            meterFactory, NAME, "ntix.payments.webhook", "Webhook de pagamento do AbacatePay",
            OUTCOME_CONFIRMED, OUTCOME_DUPLICATE, OUTCOME_ALREADY_PAID, OUTCOME_IGNORED, OUTCOME_NOT_PAID_AT_SOURCE, OUTCOME_AMOUNT_MISMATCH,
            OUTCOME_INVALID_SIGNATURE, OUTCOME_INVALID_SECRET, OUTCOME_PAYMENT_NOT_FOUND, OUTCOME_SOURCE_UNAVAILABLE, OUTCOME_INVALID_BODY, OUTCOME_CANCELED)
    {
        _paidAfterExpiration = Meter.CreateCounter<long>(
            name: "ntix.orders.paid_after_expiration",
            unit: "{order}",
            description: "Pedidos pagos depois do vencimento da reserva (RN10), para o produtor tratar.");

        _confirmationDelay = Meter.CreateHistogram<double>(
            name: "ntix.payments.pix.confirmation_delay",
            unit: "s",
            description: "Tempo entre a criação do Pix e a confirmação pelo webhook.");

        _paidAfterExpiration.Add(0);
    }

    public void PaidAfterExpiration()
        => _paidAfterExpiration.Add(1);

    public void ConfirmationDelay(TimeSpan delay)
        => _confirmationDelay.Record(delay.TotalSeconds);
}
