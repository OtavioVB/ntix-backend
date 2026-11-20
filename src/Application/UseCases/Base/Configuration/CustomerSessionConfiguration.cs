namespace Application.UseCases.Base.Configuration;

/// <summary>Sessão do cliente (REQ12, RN4): o refresh token vale <see cref="RefreshTokenDays"/> dias (padrão 30).</summary>
public sealed record CustomerSessionConfiguration
{
    public int RefreshTokenDays { get; init; } = 30;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}
