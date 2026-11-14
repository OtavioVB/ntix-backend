using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateProducerSession.Inputs;

public sealed record CreateProducerSessionUseCaseInput
{
    /// <summary>E-mail de acesso, normalizado como no cadastro (RN1).</summary>
    public EmailValueObject Email { get; set; }

    /// <summary>Senha digitada: só não pode ser vazia, sem a regra de força do cadastro.</summary>
    public PasswordValueObject Password { get; set; }

    private CreateProducerSessionUseCaseInput(EmailValueObject email, PasswordValueObject password)
    {
        Email = email;
        Password = password;
    }

    public static CreateProducerSessionUseCaseInput Create(string? email, string? password)
        => new(EmailValueObject.Build(email), PasswordValueObject.BuildForSignIn(password));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Email.MethodResult, Password.MethodResult);
}
