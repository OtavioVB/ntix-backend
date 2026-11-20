using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateOrderPayment.Inputs;

public sealed record CreateOrderPaymentUseCaseInput
{
    /// <summary>Código da rota. Texto que não tem a forma de um código responde como pedido não encontrado.</summary>
    public OrderCodeValueObject Code { get; set; }

    /// <summary>Id do cliente do access token (<c>sub</c>). Nulo quando o claim não é um GUID.</summary>
    public Guid? CustomerId { get; set; }

    public PaymentMethodValueObject Method { get; set; }

    private CreateOrderPaymentUseCaseInput(OrderCodeValueObject code, Guid? customerId, PaymentMethodValueObject method)
    {
        Code = code;
        CustomerId = customerId;
        Method = method;
    }

    public static CreateOrderPaymentUseCaseInput Create(string? code, string? customerId, string? method)
        => new(OrderCodeValueObject.Build(code), Guid.TryParse(customerId, out var parsed) ? parsed : null, PaymentMethodValueObject.Build(method));

    // O comprador não informa valor: ele é o total do pedido (RN3).
    public MethodResult GetInputValidation()
        => Method.MethodResult;
}
