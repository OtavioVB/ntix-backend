using Domain.ValueObjects;
using Infra.SqlServer.Repositories.Customers.Models;
using System.Globalization;

namespace IntegratedTests.Support;

public static class CustomerTestData
{
    private const int CPF_BASE_LENGTH = 9;

    public static string NewEmail()
        => $"{Guid.NewGuid():N}@ntix.com";

    public static string NewFullName()
        => $"Cliente {Guid.NewGuid():N}";

    public static string NewCpf()
    {
        var digits = string.Concat(Enumerable.Range(0, CPF_BASE_LENGTH).Select(_ => Random.Shared.Next(10)));

        digits += CalculateCheckDigit(digits);
        digits += CalculateCheckDigit(digits);

        return digits;
    }

    public static AuditableInfoValueObject NewAuditableInfo(Guid? correlationId = null)
        => AuditableInfoValueObject.Build(
            correlationId: (correlationId ?? Guid.NewGuid()).ToString(),
            originService: "integrated-tests",
            executionUser: "user-123",
            requestedAt: DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

    public static CustomerModel NewModel(string? email = null, string? document = null, string? slug = null)
        => new(
            Id: Guid.NewGuid(),
            FullName: NewFullName(),
            Email: email ?? NewEmail(),
            Document: document ?? NewCpf(),
            Slug: slug ?? $"cliente-{Guid.NewGuid():N}",
            CreatedAt: DateTime.UtcNow,
            CreatedCorrelationId: Guid.NewGuid());

    private static int CalculateCheckDigit(string digits)
    {
        var sum = digits.Select((digit, index) => (digit - '0') * (digits.Length + 1 - index)).Sum();
        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}
