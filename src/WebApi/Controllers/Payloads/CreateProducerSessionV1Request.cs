namespace WebApi.Controllers.Payloads;

/// <summary>Login do usuário do produtor (REQ5). A senha só é conferida, sem a regra de força do cadastro.</summary>
public sealed record CreateProducerSessionV1Request(string Email, string Password);
