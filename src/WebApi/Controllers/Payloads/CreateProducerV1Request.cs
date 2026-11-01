namespace WebApi.Controllers.Payloads;

public sealed record CreateProducerV1Request(string Name, string Email, string Phone, string UserName, string Password, string? Document = null);
