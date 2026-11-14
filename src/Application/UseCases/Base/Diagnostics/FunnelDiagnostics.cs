using System.Diagnostics.Metrics;

namespace Application.UseCases.Base.Diagnostics;

/// <summary>
/// Funil de um caso de uso no Prometheus: <c>ntix.&lt;dominio&gt;.&lt;fluxo&gt;.requests</c> na entrada e
/// <c>ntix.&lt;dominio&gt;.&lt;fluxo&gt;.outcomes</c> com a tag <c>outcome</c> em cada desfecho.
/// </summary>
public abstract class FunnelDiagnostics
{
    public const string OUTCOME_VALIDATION_FAILED = "validation_failed";
    public const string OUTCOME_ERROR = "error";

    private const string OUTCOME_TAG = "outcome";

    private readonly Counter<long> _requests;
    private readonly Counter<long> _outcomes;

    protected Meter Meter { get; }

    protected FunnelDiagnostics(IMeterFactory meterFactory, string meterName, string flowName, string description, params string[] outcomes)
    {
        Meter = meterFactory.Create(meterName);

        _requests = Meter.CreateCounter<long>(
            name: $"{flowName}.requests",
            unit: "{request}",
            description: $"{description}: solicitações recebidas pelo caso de uso.");

        _outcomes = Meter.CreateCounter<long>(
            name: $"{flowName}.outcomes",
            unit: "{request}",
            description: $"{description}: solicitações concluídas, por desfecho.");

        // O increase() do Prometheus ignora a primeira amostra de uma série: partir de zero mantém o funil exato.
        _requests.Add(0);

        foreach (var outcome in outcomes.Concat([OUTCOME_VALIDATION_FAILED, OUTCOME_ERROR]).Distinct())
            _outcomes.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
    }

    public void Requested()
        => _requests.Add(1);

    public void Finished(string outcome)
        => _outcomes.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
}
