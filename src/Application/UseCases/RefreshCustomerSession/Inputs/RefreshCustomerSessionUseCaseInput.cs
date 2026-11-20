using Domain.ValueObjects;

namespace Application.UseCases.RefreshCustomerSession.Inputs;

public sealed record RefreshCustomerSessionUseCaseInput
{
    /// <summary>Refresh token recebido. Fora do formato, é tratado como inexistente (401), não como dado inválido.</summary>
    public RefreshTokenValueObject RefreshToken { get; set; }

    private RefreshCustomerSessionUseCaseInput(RefreshTokenValueObject refreshToken)
    {
        RefreshToken = refreshToken;
    }

    public static RefreshCustomerSessionUseCaseInput Create(string? refreshToken)
        => new(RefreshTokenValueObject.Build(refreshToken));
}
