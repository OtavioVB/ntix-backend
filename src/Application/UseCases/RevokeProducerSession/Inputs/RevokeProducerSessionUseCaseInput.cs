using Domain.ValueObjects;

namespace Application.UseCases.RevokeProducerSession.Inputs;

public sealed record RevokeProducerSessionUseCaseInput
{
    /// <summary>Refresh token da sessão a encerrar. Fora do formato, é tratado como inexistente: o logout é idempotente.</summary>
    public RefreshTokenValueObject RefreshToken { get; set; }

    private RevokeProducerSessionUseCaseInput(RefreshTokenValueObject refreshToken)
    {
        RefreshToken = refreshToken;
    }

    public static RevokeProducerSessionUseCaseInput Create(string? refreshToken)
        => new(RefreshTokenValueObject.Build(refreshToken));
}
