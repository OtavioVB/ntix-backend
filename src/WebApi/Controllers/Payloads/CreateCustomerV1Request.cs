namespace WebApi.Controllers.Payloads;

public sealed record CreateCustomerV1Request(string FullName, string Email, string Document);
