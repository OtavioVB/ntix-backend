using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Customers.Models;
using System.Security.Cryptography;

namespace UnitTests.Support;

/// <summary>Dados da sessão do cliente (REQ12) para os testes unitários. Reaproveita a chave e o relógio do REQ5.</summary>
public static class CustomerSessionTestData
{
    public const string EMAIL = "marina@exemplo.com";
    public const string CPF = "52998224725";
    public const string MASKED_CPF = "529.982.247-25";
    public const string PASSWORD = ProducerSessionTestData.PASSWORD;

    private static readonly byte[] SALT = RandomNumberGenerator.GetBytes(PasswordSaltValueObject.LENGTH);

    // O hash leva centenas de milissegundos: é calculado uma vez para todos os testes.
    private static readonly Lazy<byte[]> PASSWORD_HASH = new(() => PasswordHashValueObject
        .Build(PasswordValueObject.Build(PASSWORD), PasswordSaltValueObject.Build(SALT), ProducerSessionTestData.PRIVATE_KEY)
        .Value);

    public static CustomerModel NewCustomer(Guid? id = null, string? phone = "81999990000")
        => new(
            Id: id ?? Guid.NewGuid(),
            FullName: "Marina Souza",
            Email: EMAIL,
            Document: CPF,
            Slug: "marina-souza",
            PasswordHash: PASSWORD_HASH.Value,
            PasswordSalt: SALT,
            PhoneNumber: phone,
            CreatedAt: ProducerSessionTestData.NOW.UtcDateTime.AddDays(-1),
            CreatedCorrelationId: Guid.NewGuid());

    public static CustomerSessionModel NewSession(Guid customerId, byte[] refreshTokenHash, DateTime? expiresAt = null, DateTime? revokedAt = null, Guid? replacedById = null)
        => new(
            Id: Guid.NewGuid(),
            CustomerId: customerId,
            FamilyId: Guid.NewGuid(),
            RefreshTokenHash: refreshTokenHash,
            ExpiresAt: expiresAt ?? ProducerSessionTestData.NOW.UtcDateTime.AddDays(20),
            CreatedAt: ProducerSessionTestData.NOW.UtcDateTime.AddDays(-1),
            CreatedCorrelationId: Guid.NewGuid(),
            RevokedAt: revokedAt,
            ReplacedById: replacedById);
}
