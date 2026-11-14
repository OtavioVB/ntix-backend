using Domain.CrossCutting.MethodResultContext;

namespace Application.UseCases.GetPaymentProviders.Inputs;

/// <summary>Consulta do catálogo: não tem dado de entrada além das informações auditáveis.</summary>
public sealed record GetPaymentProvidersUseCaseInput
{
    private GetPaymentProvidersUseCaseInput()
    {
    }

    public static GetPaymentProvidersUseCaseInput Create()
        => new();

    public MethodResult GetInputValidation()
        => MethodResult.Success();
}
