using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateProducer.Inputs;

public sealed record CreateProducerUseCaseInput
{
    public ProducerNameValueObject Name { get; set; }
    public EmailValueObject Email { get; set; }
    public E164PhoneValueObject Phone { get; set; }
    public ProducerDocumentValueObject Document { get; set; }
    public FullNameValueObject UserName { get; set; }
    public PasswordValueObject Password { get; set; }

    private CreateProducerUseCaseInput(ProducerNameValueObject name, EmailValueObject email, E164PhoneValueObject phone, ProducerDocumentValueObject document, FullNameValueObject userName, PasswordValueObject password)
    {
        Name = name;
        Email = email;
        Phone = phone;
        Document = document;
        UserName = userName;
        Password = password;
    }

    public static CreateProducerUseCaseInput Create(ProducerNameValueObject name, EmailValueObject email, E164PhoneValueObject phone, ProducerDocumentValueObject document, FullNameValueObject userName, PasswordValueObject password)
        => new(name, email, phone, document, userName, password);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Name.MethodResult, Email.MethodResult, Phone.MethodResult, Document.MethodResult, UserName.MethodResult, Password.MethodResult);
}
