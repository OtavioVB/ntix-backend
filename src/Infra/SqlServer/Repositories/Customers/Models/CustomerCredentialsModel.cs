namespace Infra.SqlServer.Repositories.Customers.Models;

public sealed record CustomerCredentialsModel(
    Guid Id,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    bool IsLinkedToProducer);
