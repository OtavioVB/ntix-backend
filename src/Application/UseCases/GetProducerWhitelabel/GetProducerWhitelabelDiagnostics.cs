using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.GetProducerWhitelabel;

public sealed class GetProducerWhitelabelDiagnostics
{
    public const string NAME = "Ntix.Producers";

    public const string OUTCOME_SERVED = "served";
    public const string OUTCOME_VALIDATION_FAILED = "validation_failed";
    public const string OUTCOME_NOT_FOUND = "not_found";
    public const string OUTCOME_INVALID_FILE = "invalid_file";
    public const string OUTCOME_ERROR = "error";

    private const string OUTCOME_TAG = "outcome";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _requests;
    private readonly Counter<long> _outcomes;

    public GetProducerWhitelabelDiagnostics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(NAME);

        _requests = meter.CreateCounter<long>(
            name: "ntix.producers.whitelabel.requests",
            unit: "{request}",
            description: "Consultas da configuração whitelabel recebidas pelo caso de uso.");

        _outcomes = meter.CreateCounter<long>(
            name: "ntix.producers.whitelabel.outcomes",
            unit: "{request}",
            description: "Consultas da configuração whitelabel concluídas, por desfecho.");

        // O increase() do Prometheus ignora a primeira amostra de uma série: partir de zero mantém o funil exato.
        _requests.Add(0);

        foreach (var outcome in new[] { OUTCOME_SERVED, OUTCOME_VALIDATION_FAILED, OUTCOME_NOT_FOUND, OUTCOME_INVALID_FILE, OUTCOME_ERROR })
            _outcomes.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
    }

    public void Requested()
        => _requests.Add(1);

    public void Finished(string outcome)
        => _outcomes.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
}
