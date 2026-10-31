using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateCustomer.Inputs;

public sealed record CreateCustomerUseCaseInput
{
    public FullNameValueObject FullName { get; set; }
    public EmailValueObject Email { get; set; }
    public CpfValueObject Document { get; set; }

    private CreateCustomerUseCaseInput(FullNameValueObject fullName, EmailValueObject email, CpfValueObject document)
    {
        FullName = fullName;
        Email = email;
        Document = document;
    }

    public static CreateCustomerUseCaseInput Create(FullNameValueObject fullName, EmailValueObject email, CpfValueObject document)
        => new(fullName, email, document);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(FullName.MethodResult, Email.MethodResult, Document.MethodResult);
}
