using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateCustomer;

public sealed class CreateCustomerDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_VALIDATION_FAILED = "validation_failed";
    public const string OUTCOME_ALREADY_EXISTS = "already_exists";
    public const string OUTCOME_ERROR = "error";

    private const string OUTCOME_TAG = "outcome";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _requests;
    private readonly Counter<long> _outcomes;
    private readonly Histogram<double> _passwordHashDuration;

    public CreateCustomerDiagnostics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(NAME);

        _requests = meter.CreateCounter<long>(
            name: "ntix.customers.registration.requests",
            unit: "{request}",
            description: "Solicitações de cadastro de cliente recebidas pelo caso de uso.");

        _outcomes = meter.CreateCounter<long>(
            name: "ntix.customers.registration.outcomes",
            unit: "{request}",
            description: "Solicitações de cadastro de cliente concluídas, por desfecho.");

        _passwordHashDuration = meter.CreateHistogram<double>(
            name: "ntix.customers.password.hash.duration",
            unit: "s",
            description: "Tempo das iterações de hash na definição da senha do cliente.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.15, 0.2, 0.25, 0.35, 0.5, 0.75, 1, 2, 5] });

        // O increase() do Prometheus ignora a primeira amostra de uma série: partir de zero mantém o funil exato.
        _requests.Add(0);

        foreach (var outcome in new[] { OUTCOME_CREATED, OUTCOME_VALIDATION_FAILED, OUTCOME_ALREADY_EXISTS, OUTCOME_ERROR })
            _outcomes.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
    }

    public void RegistrationRequested()
        => _requests.Add(1);

    public void RegistrationFinished(string outcome)
        => _outcomes.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));

    public void PasswordHashed(double seconds)
        => _passwordHashDuration.Record(seconds);
}
