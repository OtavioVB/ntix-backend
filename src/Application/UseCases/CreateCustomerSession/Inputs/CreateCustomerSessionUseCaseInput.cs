using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateCustomerSession.Inputs;

public sealed record CreateCustomerSessionUseCaseInput
{
    /// <summary>E-mail ou CPF (com ou sem máscara) da conta (RN1).</summary>
    public CustomerLoginValueObject Login { get; set; }

    /// <summary>Só obrigatoriedade: a regra de força da senha é do cadastro, não do login.</summary>
    public PasswordValueObject Password { get; set; }

    private CreateCustomerSessionUseCaseInput(CustomerLoginValueObject login, PasswordValueObject password)
    {
        Login = login;
        Password = password;
    }

    public static CreateCustomerSessionUseCaseInput Create(string? login, string? password)
        => new(CustomerLoginValueObject.Build(login), PasswordValueObject.BuildForSignIn(password));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Login.MethodResult, Password.MethodResult);
}
