using Domain.CrossCutting.MethodResultContext;

namespace Application.UseCases.GetPublicEvent.Inputs;

public sealed record GetPublicEventUseCaseInput
{
    /// <summary>Slug do evento na rota. Texto que não é um slug existente responde como evento não encontrado.</summary>
    public string EventSlug { get; set; }

    private GetPublicEventUseCaseInput(string eventSlug)
    {
        EventSlug = eventSlug;
    }

    public static GetPublicEventUseCaseInput Create(string? eventSlug)
        => new((eventSlug ?? string.Empty).Trim());

    // O slug não é validado na entrada: qualquer valor que não exista vira 404, sem revelar o formato esperado.
    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
