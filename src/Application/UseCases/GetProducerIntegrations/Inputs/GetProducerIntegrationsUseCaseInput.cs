using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetProducerIntegrations.Inputs;

public sealed record GetProducerIntegrationsUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    private GetProducerIntegrationsUseCaseInput(SlugValueObject producerSlug)
    {
        ProducerSlug = producerSlug;
    }

    public static GetProducerIntegrationsUseCaseInput Create(string? producerSlug)
        => new(SlugValueObject.Parse(producerSlug));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult);
}
