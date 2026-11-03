using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateProducer.Inputs;

public sealed record CreateProducerUseCaseInput
{
    public ProducerNameValueObject Name { get; set; }

    /// <summary>E-mail da produtora e, também, de acesso do administrador.</summary>
    public EmailValueObject Email { get; set; }
    public PhoneValueObject Phone { get; set; }
    public FullNameValueObject AdministratorName { get; set; }
    public PasswordValueObject AdministratorPassword { get; set; }

    private CreateProducerUseCaseInput(ProducerNameValueObject name, EmailValueObject email, PhoneValueObject phone, FullNameValueObject administratorName, PasswordValueObject administratorPassword)
    {
        Name = name;
        Email = email;
        Phone = phone;
        AdministratorName = administratorName;
        AdministratorPassword = administratorPassword;
    }

    public static CreateProducerUseCaseInput Create(ProducerNameValueObject name, EmailValueObject email, PhoneValueObject phone, FullNameValueObject administratorName, PasswordValueObject administratorPassword)
        => new(name, email, phone, administratorName, administratorPassword);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Name.MethodResult, Email.MethodResult, Phone.MethodResult, AdministratorName.MethodResult, AdministratorPassword.MethodResult);
}
