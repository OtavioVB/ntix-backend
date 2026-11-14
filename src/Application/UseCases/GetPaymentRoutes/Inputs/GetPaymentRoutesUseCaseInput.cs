using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetPaymentRoutes.Inputs;

public sealed record GetPaymentRoutesUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    private GetPaymentRoutesUseCaseInput(SlugValueObject producerSlug)
    {
        ProducerSlug = producerSlug;
    }

    public static GetPaymentRoutesUseCaseInput Create(string? producerSlug)
        => new(SlugValueObject.Parse(producerSlug));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult);
}
