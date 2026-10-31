using System.Diagnostics.Metrics;

namespace UnitTests.Support;

public sealed record CapturedMeasurement(string Instrument, long Value, string? Outcome);

public sealed class TestMeterFactory : IMeterFactory
{
    public List<Meter> Meters { get; } = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options);

        Meters.Add(meter);

        return meter;
    }

    public void Dispose()
        => Meters.ForEach(meter => meter.Dispose());
}

public sealed class MeasurementCollector : IDisposable
{
    private readonly MeterListener _listener = new();

    public List<CapturedMeasurement> Measurements { get; } = [];

    public MeasurementCollector(TestMeterFactory meterFactory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (meterFactory.Meters.Contains(instrument.Meter))
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? outcome = null;

            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                    outcome = tag.Value as string;
            }

            Measurements.Add(new CapturedMeasurement(instrument.Name, value, outcome));
        });

        _listener.Start();
    }

    public void Dispose()
        => _listener.Dispose();
}
