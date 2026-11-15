using Domain.ValueObjects;

namespace Application.UseCases.RefreshProducerSession.Inputs;

public sealed record RefreshProducerSessionUseCaseInput
{
    /// <summary>Refresh token recebido. Fora do formato, é tratado como inexistente (401), não como dado inválido.</summary>
    public RefreshTokenValueObject RefreshToken { get; set; }

    private RefreshProducerSessionUseCaseInput(RefreshTokenValueObject refreshToken)
    {
        RefreshToken = refreshToken;
    }

    public static RefreshProducerSessionUseCaseInput Create(string? refreshToken)
        => new(RefreshTokenValueObject.Build(refreshToken));
}
