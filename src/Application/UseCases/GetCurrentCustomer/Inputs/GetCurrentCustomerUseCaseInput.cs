using Domain.CrossCutting.MethodResultContext;

namespace Application.UseCases.GetCurrentCustomer.Inputs;

public sealed record GetCurrentCustomerUseCaseInput
{
    /// <summary>Id do cliente do access token (<c>sub</c>). Nulo quando o claim não é um GUID.</summary>
    public Guid? CustomerId { get; set; }

    private GetCurrentCustomerUseCaseInput(Guid? customerId)
    {
        CustomerId = customerId;
    }

    public static GetCurrentCustomerUseCaseInput Create(string? customerId)
        => new(Guid.TryParse(customerId, out var parsed) ? parsed : null);

    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
