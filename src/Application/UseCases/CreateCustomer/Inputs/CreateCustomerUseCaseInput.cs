using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateCustomer.Inputs;

public sealed record CreateCustomerUseCaseInput
{
    public FullNameValueObject FullName { get; set; }
    public EmailValueObject Email { get; set; }
    public CpfValueObject Document { get; set; }
    public PasswordValueObject Password { get; set; }
    public PhoneNumberValueObject PhoneNumber { get; set; }

    private CreateCustomerUseCaseInput(FullNameValueObject fullName, EmailValueObject email, CpfValueObject document, PasswordValueObject password, PhoneNumberValueObject phoneNumber)
    {
        FullName = fullName;
        Email = email;
        Document = document;
        Password = password;
        PhoneNumber = phoneNumber;
    }

    public static CreateCustomerUseCaseInput Create(FullNameValueObject fullName, EmailValueObject email, CpfValueObject document, PasswordValueObject password, PhoneNumberValueObject phoneNumber)
        => new(fullName, email, document, password, phoneNumber);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(FullName.MethodResult, Email.MethodResult, Document.MethodResult, Password.MethodResult, PhoneNumber.MethodResult);
}
