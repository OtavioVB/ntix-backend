using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.AuthenticateCustomer;

public sealed class AuthenticateCustomerDiagnostics
{
    public const string NAME = "Ntix.Customers";

    public const string OUTCOME_AUTHENTICATED = "authenticated";
    public const string OUTCOME_VALIDATION_FAILED = "validation_failed";
    public const string OUTCOME_CUSTOMER_NOT_FOUND = "customer_not_found";
    public const string OUTCOME_INVALID_PASSWORD = "invalid_password";
    public const string OUTCOME_NOT_LINKED_TO_PRODUCER = "not_linked_to_producer";
    public const string OUTCOME_ERROR = "error";

    private const string OUTCOME_TAG = "outcome";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _requests;
    private readonly Counter<long> _outcomes;

    public AuthenticateCustomerDiagnostics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(NAME);

        _requests = meter.CreateCounter<long>(
            name: "ntix.customers.authentication.requests",
            unit: "{request}",
            description: "Solicitações de autenticação de cliente recebidas pelo caso de uso.");

        _outcomes = meter.CreateCounter<long>(
            name: "ntix.customers.authentication.outcomes",
            unit: "{request}",
            description: "Solicitações de autenticação de cliente concluídas, por desfecho.");

        // O increase() do Prometheus ignora a primeira amostra de uma série: partir de zero mantém o funil exato.
        _requests.Add(0);

        foreach (var outcome in new[] { OUTCOME_AUTHENTICATED, OUTCOME_VALIDATION_FAILED, OUTCOME_CUSTOMER_NOT_FOUND, OUTCOME_INVALID_PASSWORD, OUTCOME_NOT_LINKED_TO_PRODUCER, OUTCOME_ERROR })
            _outcomes.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
    }

    public void AuthenticationRequested()
        => _requests.Add(1);

    public void AuthenticationFinished(string outcome)
        => _outcomes.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
}
