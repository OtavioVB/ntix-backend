using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.RankPaymentIntegrations.Inputs;

public sealed record RankPaymentIntegrationsUseCaseInput
{
    /// <summary>Produtora dona do pedido (REQ6/REQ7). Quem chama já tem o id, não o slug.</summary>
    public IdValueObject ProducerId { get; set; }

    public PaymentMethodValueObject PaymentMethod { get; set; }

    private RankPaymentIntegrationsUseCaseInput(IdValueObject producerId, PaymentMethodValueObject paymentMethod)
    {
        ProducerId = producerId;
        PaymentMethod = paymentMethod;
    }

    public static RankPaymentIntegrationsUseCaseInput Create(Guid producerId, PaymentMethodValueObject paymentMethod)
        => new(IdValueObject.Build(producerId.ToString()), paymentMethod);

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerId.MethodResult, PaymentMethod.MethodResult);
}
