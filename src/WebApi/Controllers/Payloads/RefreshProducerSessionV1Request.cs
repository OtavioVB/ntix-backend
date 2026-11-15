namespace WebApi.Controllers.Payloads;

/// <summary>Renovação da sessão do usuário do produtor (REQ5) com o refresh token do login ou da última renovação.</summary>
public sealed record RefreshProducerSessionV1Request(string RefreshToken);
