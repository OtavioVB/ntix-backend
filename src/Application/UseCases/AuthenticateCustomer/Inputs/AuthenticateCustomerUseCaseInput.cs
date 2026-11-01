using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;

namespace Application.UseCases.AuthenticateCustomer.Inputs;

public sealed record AuthenticateCustomerUseCaseInput
{
    private const string LOGIN_REQUIRED_NOTIFICATION_CODE = "LOGIN_REQUIRED";
    private const string LOGIN_REQUIRED_NOTIFICATION_MESSAGE = "Informe o email ou o CPF.";

    private const string LOGIN_MUST_BE_ONLY_ONE_NOTIFICATION_CODE = "LOGIN_MUST_BE_ONLY_ONE";
    private const string LOGIN_MUST_BE_ONLY_ONE_NOTIFICATION_MESSAGE = "Informe apenas o email ou apenas o CPF.";

    private const string PRODUCER_ID_REQUIRED_NOTIFICATION_CODE = "PRODUCER_ID_REQUIRED";
    private const string PRODUCER_ID_REQUIRED_NOTIFICATION_MESSAGE = "O ID do produtor é obrigatório.";

    public EmailValueObject? Email { get; }
    public CpfValueObject? Document { get; }
    public PasswordValueObject Password { get; }
    public IdValueObject ProducerId { get; }

    private readonly bool _hasProducerId;

    private AuthenticateCustomerUseCaseInput(EmailValueObject? email, CpfValueObject? document, PasswordValueObject password, IdValueObject producerId, bool hasProducerId)
    {
        Email = email;
        Document = document;
        Password = password;
        ProducerId = producerId;
        _hasProducerId = hasProducerId;
    }

    public static AuthenticateCustomerUseCaseInput Create(string? email, string? document, PasswordValueObject password, string? producerId)
        => new(
            email: string.IsNullOrWhiteSpace(email) ? null : EmailValueObject.Build(email),
            document: string.IsNullOrWhiteSpace(document) ? null : CpfValueObject.Build(document),
            password: password,
            // IdValueObject gera um novo ID quando recebe vazio, o que aqui esconderia a ausência do produtor.
            producerId: IdValueObject.Build(producerId),
            hasProducerId: !string.IsNullOrWhiteSpace(producerId));

    public MethodResult GetInputValidation()
    {
        var results = new List<MethodResult>();

        if (Email is null && Document is null)
            results.Add(BuildError(LOGIN_REQUIRED_NOTIFICATION_CODE, LOGIN_REQUIRED_NOTIFICATION_MESSAGE));

        if (Email is not null && Document is not null)
            results.Add(BuildError(LOGIN_MUST_BE_ONLY_ONE_NOTIFICATION_CODE, LOGIN_MUST_BE_ONLY_ONE_NOTIFICATION_MESSAGE));

        if (Email is not null)
            results.Add(Email.Value.MethodResult);

        if (Document is not null)
            results.Add(Document.Value.MethodResult);

        results.Add(Password.MethodResult);

        results.Add(_hasProducerId
            ? ProducerId.MethodResult
            : BuildError(PRODUCER_ID_REQUIRED_NOTIFICATION_CODE, PRODUCER_ID_REQUIRED_NOTIFICATION_MESSAGE));

        return MethodResult.Factory(results.ToArray());
    }

    public string GetObfuscatedLogin()
        => Email?.GetObfuscatedValue() ?? Document!.Value.GetObfuscatedValue();

    private static MethodResult BuildError(string code, string message)
        => MethodResult.Error([Notification.Error(code: code, message: message)]);
}
