using System.Diagnostics;

namespace UnitTests.Support;

/// <summary>
/// Ouve os spans de uma <see cref="ActivitySource"/> durante o teste, como o OpenTelemetry faria em produção. Sem
/// ouvinte, <c>StartActivity</c> devolve nulo e as tags do span não são verificáveis.
/// </summary>
public sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public ActivityCollector(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_activities)
                    _activities.Add(activity);
            },
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Span já encerrado com o nome informado e o <c>ntix.correlation_id</c> do teste.</summary>
    public Activity Single(string operationName, string correlationId)
    {
        lock (_activities)
        {
            return Assert.Single(_activities, activity =>
                activity.OperationName == operationName
                && Equals(activity.GetTagItem("ntix.correlation_id")?.ToString(), correlationId));
        }
    }

    public void Dispose()
        => _listener.Dispose();
}
