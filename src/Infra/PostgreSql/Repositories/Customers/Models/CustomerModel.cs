namespace Infra.PostgreSql.Repositories.Customers.Models;

public sealed record CustomerModel(
    Guid Id,
    string FullName,
    string Email,
    string Document,
    string Slug,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    string? PhoneNumber,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);
