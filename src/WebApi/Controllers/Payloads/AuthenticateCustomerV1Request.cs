namespace WebApi.Controllers.Payloads;

public sealed record AuthenticateCustomerV1Request(string? Email, string? Document, string? Password, string? ProducerId);
