namespace WebApi.Controllers.Payloads;

/// <summary>Login do cliente (REQ12): e-mail ou CPF (com ou sem máscara) e senha.</summary>
public sealed record CreateCustomerSessionV1Request(string? Login, string? Password);

/// <summary>Renovação ou logout do cliente com o refresh token do login ou da última renovação.</summary>
public sealed record CustomerRefreshTokenV1Request(string? RefreshToken);
