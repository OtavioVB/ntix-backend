namespace Application.UseCases.AuthenticateCustomer.Outputs;

public sealed record AuthenticateCustomerUseCaseOutput(string AccessToken, DateTime ExpiresAt);
