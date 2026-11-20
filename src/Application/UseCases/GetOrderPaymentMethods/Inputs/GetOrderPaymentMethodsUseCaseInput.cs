using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetOrderPaymentMethods.Inputs;

public sealed record GetOrderPaymentMethodsUseCaseInput
{
    /// <summary>Código da rota. Texto que não tem a forma de um código responde como pedido não encontrado.</summary>
    public OrderCodeValueObject Code { get; set; }

    /// <summary>Id do cliente do access token (<c>sub</c>). Nulo quando o claim não é um GUID.</summary>
    public Guid? CustomerId { get; set; }

    private GetOrderPaymentMethodsUseCaseInput(OrderCodeValueObject code, Guid? customerId)
    {
        Code = code;
        CustomerId = customerId;
    }

    public static GetOrderPaymentMethodsUseCaseInput Create(string? code, string? customerId)
        => new(OrderCodeValueObject.Build(code), Guid.TryParse(customerId, out var parsed) ? parsed : null);

    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
