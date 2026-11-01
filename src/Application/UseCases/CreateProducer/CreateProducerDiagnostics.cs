using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application.UseCases.CreateProducer;

public sealed class CreateProducerDiagnostics
{
    public const string NAME = "Ntix.Producers";

    public const string OUTCOME_CREATED = "created";
    public const string OUTCOME_VALIDATION_FAILED = "validation_failed";
    public const string OUTCOME_ALREADY_EXISTS = "already_exists";
    public const string OUTCOME_ERROR = "error";

    public const string VERIFICATION_EMAIL_SENT = "sent";
    public const string VERIFICATION_EMAIL_FAILED = "failed";

    private const string OUTCOME_TAG = "outcome";

    public static readonly ActivitySource ACTIVITY_SOURCE = new(NAME);

    private readonly Counter<long> _requests;
    private readonly Counter<long> _outcomes;
    private readonly Counter<long> _verificationEmails;
    private readonly Histogram<double> _passwordHashDuration;

    public CreateProducerDiagnostics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(NAME);

        _requests = meter.CreateCounter<long>(
            name: "ntix.producers.registration.requests",
            unit: "{request}",
            description: "Solicitações de cadastro de produtor recebidas pelo caso de uso.");

        _outcomes = meter.CreateCounter<long>(
            name: "ntix.producers.registration.outcomes",
            unit: "{request}",
            description: "Solicitações de cadastro de produtor concluídas, por desfecho.");

        _verificationEmails = meter.CreateCounter<long>(
            name: "ntix.producers.registration.verification_emails",
            unit: "{email}",
            description: "Emails de verificação do cadastro de produtor, por desfecho do envio.");

        _passwordHashDuration = meter.CreateHistogram<double>(
            name: "ntix.producers.password.hash.duration",
            unit: "s",
            description: "Tempo das iterações de hash na definição da senha do usuário do produtor.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.15, 0.2, 0.25, 0.35, 0.5, 0.75, 1, 2, 5] });

        // O increase() do Prometheus ignora a primeira amostra de uma série: partir de zero mantém o funil exato.
        _requests.Add(0);

        foreach (var outcome in new[] { OUTCOME_CREATED, OUTCOME_VALIDATION_FAILED, OUTCOME_ALREADY_EXISTS, OUTCOME_ERROR })
            _outcomes.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));

        foreach (var outcome in new[] { VERIFICATION_EMAIL_SENT, VERIFICATION_EMAIL_FAILED })
            _verificationEmails.Add(0, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));
    }

    public void RegistrationRequested()
        => _requests.Add(1);

    public void RegistrationFinished(string outcome)
        => _outcomes.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));

    public void VerificationEmailFinished(string outcome)
        => _verificationEmails.Add(1, new KeyValuePair<string, object?>(OUTCOME_TAG, outcome));

    public void PasswordHashed(double seconds)
        => _passwordHashDuration.Record(seconds);
}
