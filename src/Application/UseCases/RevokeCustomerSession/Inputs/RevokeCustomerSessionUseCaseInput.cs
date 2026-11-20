using Domain.ValueObjects;

namespace Application.UseCases.RevokeCustomerSession.Inputs;

public sealed record RevokeCustomerSessionUseCaseInput
{
    /// <summary>Refresh token da sessão a encerrar. Fora do formato, é tratado como inexistente: o logout é idempotente.</summary>
    public RefreshTokenValueObject RefreshToken { get; set; }

    private RevokeCustomerSessionUseCaseInput(RefreshTokenValueObject refreshToken)
    {
        RefreshToken = refreshToken;
    }

    public static RevokeCustomerSessionUseCaseInput Create(string? refreshToken)
        => new(RefreshTokenValueObject.Build(refreshToken));
}
