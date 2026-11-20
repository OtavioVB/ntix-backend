using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetCustomerOrder.Inputs;

public sealed record GetCustomerOrderUseCaseInput
{
    /// <summary>Código da rota. Texto que não tem a forma de um código responde como pedido não encontrado.</summary>
    public OrderCodeValueObject Code { get; set; }

    /// <summary>Id do cliente do access token (<c>sub</c>). Nulo quando o claim não é um GUID.</summary>
    public Guid? CustomerId { get; set; }

    private GetCustomerOrderUseCaseInput(OrderCodeValueObject code, Guid? customerId)
    {
        Code = code;
        CustomerId = customerId;
    }

    public static GetCustomerOrderUseCaseInput Create(string? code, string? customerId)
        => new(OrderCodeValueObject.Build(code), Guid.TryParse(customerId, out var parsed) ? parsed : null);

    // Código inválido vira 404, sem revelar o formato esperado.
    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
