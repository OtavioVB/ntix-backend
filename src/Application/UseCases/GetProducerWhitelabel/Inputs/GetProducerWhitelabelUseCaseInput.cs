using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.GetProducerWhitelabel.Inputs;

public sealed record GetProducerWhitelabelUseCaseInput
{
    public SlugValueObject Slug { get; set; }

    private GetProducerWhitelabelUseCaseInput(SlugValueObject slug)
    {
        Slug = slug;
    }

    /// <summary>O slug vem da rota e é recebido sem normalização: só a forma canônica é aceita.</summary>
    public static GetProducerWhitelabelUseCaseInput Create(string? slug)
        => new(SlugValueObject.Parse(slug));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(Slug.MethodResult);
}
