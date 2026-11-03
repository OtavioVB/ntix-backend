using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Producers.Models;
using System.Globalization;
using System.Security.Cryptography;

namespace IntegratedTests.Support;

public static class ProducerTestData
{
    public const string PASSWORD = "Senha@123";
    public const string PHONE = "(11) 98765-4321";
    public const string E164_PHONE = "+5511987654321";

    public static string NewEmail()
        => $"{Guid.NewGuid():N}@produtora.com";

    // Nome único: os testes não dependem da ordem nem dos slugs de outros testes.
    public static string NewName()
        => $"Produtora {Guid.NewGuid():N}";

    public static AuditableInfoValueObject NewAuditableInfo(Guid? correlationId = null)
        => AuditableInfoValueObject.Build(
            correlationId: (correlationId ?? Guid.NewGuid()).ToString(),
            originService: "integrated-tests",
            executionUser: null,
            requestedAt: DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture));

    public static ProducerModel NewModel(string? email = null, string? slug = null)
        => new(
            Id: Guid.NewGuid(),
            Name: NewName(),
            Email: email ?? NewEmail(),
            Phone: E164_PHONE,
            Slug: slug ?? $"p-{Guid.NewGuid():N}",
            Status: "PENDING",
            CreatedAt: DateTime.UtcNow,
            CreatedCorrelationId: Guid.NewGuid());

    public static ProducerUserModel NewUserModel(Guid producerId, string? email = null)
        => new(
            Id: Guid.NewGuid(),
            ProducerId: producerId,
            Name: "Maria da Silva",
            Email: email ?? NewEmail(),
            PasswordHash: RandomNumberGenerator.GetBytes(PasswordHashValueObject.LENGTH),
            PasswordSalt: RandomNumberGenerator.GetBytes(PasswordSaltValueObject.LENGTH),
            UserType: "ADMINISTRATOR",
            Status: "PENDING",
            CreatedAt: DateTime.UtcNow,
            CreatedCorrelationId: Guid.NewGuid());
}
