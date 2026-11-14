namespace Application.UseCases.Base.Configuration;

/// <summary>Limite de tentativas de login por e-mail (REQ5, RN12): <see cref="PermitLimit"/> a cada <see cref="WindowMinutes"/>.</summary>
public sealed record LoginAttemptConfiguration(int PermitLimit = 10, int WindowMinutes = 15)
{
    public TimeSpan Window => TimeSpan.FromMinutes(WindowMinutes);
}
