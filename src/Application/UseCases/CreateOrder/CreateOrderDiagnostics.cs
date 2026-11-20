using Application.UseCases.Base.Diagnostics;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateOrder;

public sealed class CreateOrderDiagnostics : FunnelDiagnostics
{
    public const string NAME = "Ntix.Orders";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_EVENT_NOT_FOUND = "event_not_found";
    public const string OUTCOME_NOT_ON_SALE = "not_on_sale";
    public const string OUTCOME_INSUFFICIENT_AVAILABILITY = "insufficient_availability";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Histogram<double> _batchLockDuration;

    public CreateOrderDiagnostics(IMeterFactory meterFactory)
        : base(meterFactory, NAME, "ntix.orders.create", "Criação de pedidos", OUTCOME_CREATED, OUTCOME_EVENT_NOT_FOUND, OUTCOME_NOT_ON_SALE, OUTCOME_INSUFFICIENT_AVAILABILITY)
    {
        _batchLockDuration = Meter.CreateHistogram<double>(
            name: "ntix.orders.batch_lock.duration",
            unit: "s",
            description: "Tempo esperando a trava dos lotes do pedido: cresce quando muitos compradores disputam o mesmo lote.");
    }

    public void BatchLockAcquired(TimeSpan duration)
        => _batchLockDuration.Record(duration.TotalSeconds);
}
