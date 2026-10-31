namespace Infra.SqlServer.Repositories.Customers.Models;

public sealed record CustomerModel(
    Guid Id,
    string FullName,
    string Email,
    string Document,
    string Slug,
    DateTime CreatedAt,
    Guid CreatedCorrelationId);
