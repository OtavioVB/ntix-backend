namespace WebApi.Controllers.Payloads;

public sealed record AuthenticateCustomerV1Response(string AccessToken, string TokenType, DateTime ExpiresAt);
