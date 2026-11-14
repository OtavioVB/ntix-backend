namespace WebApi.Controllers.Payloads;

/// <summary>Logout do usuário do produtor (REQ5): revoga a família inteira do refresh token.</summary>
public sealed record RevokeProducerSessionV1Request(string RefreshToken);
