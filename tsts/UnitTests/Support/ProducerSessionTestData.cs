using Application.UseCases.Base.Configuration;
using Domain.ValueObjects;
using Infra.PostgreSql.Repositories.Producers.Models;
using System.Security.Cryptography;

namespace UnitTests.Support;

/// <summary>Dados da sessão do usuário do produtor (REQ5) para os testes unitários.</summary>
public static class ProducerSessionTestData
{
    public const string EMAIL = "ana@produtora.com.br";
    public const string OBFUSCATED_EMAIL = "an***@produtora.com.br";
    public const string PASSWORD = "Senha@123";
    public const string PRIVATE_KEY = "chave-de-teste";
    public const string PRODUCER_SLUG = "produtora-x";
    public const string USER_NAME = "Ana Souza";

    public const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    public const string ORIGIN_SERVICE = "ntix-web";
    public const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    public static readonly DateTimeOffset NOW = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public static readonly JwtConfiguration JWT_CONFIGURATION = new(
        SigningKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        Issuer: "https://api.ntix.test",
        Audience: "ntix-producer-panel");

    public static readonly Guid PRODUCER_ID = Guid.NewGuid();

    private static readonly byte[] SALT = RandomNumberGenerator.GetBytes(PasswordSaltValueObject.LENGTH);

    // O hash leva centenas de milissegundos: é calculado uma vez para todos os testes.
    private static readonly Lazy<byte[]> PASSWORD_HASH = new(() => PasswordHashValueObject
        .Build(PasswordValueObject.Build(PASSWORD), PasswordSaltValueObject.Build(SALT), PRIVATE_KEY)
        .Value);

    public static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT);

    public static ProducerUserModel NewUser(string status = "PENDING", Guid? id = null)
        => new(
            Id: id ?? Guid.NewGuid(),
            ProducerId: PRODUCER_ID,
            Name: USER_NAME,
            Email: EMAIL,
            PasswordHash: PASSWORD_HASH.Value,
            PasswordSalt: SALT,
            UserType: "ADMINISTRATOR",
            Status: status,
            CreatedAt: NOW.UtcDateTime.AddDays(-1),
            CreatedCorrelationId: Guid.NewGuid());

    public static ProducerModel NewProducer(string slug = PRODUCER_SLUG)
        => new(
            Id: PRODUCER_ID,
            Name: "Produtora X",
            Email: EMAIL,
            Phone: "+5511987654321",
            Slug: slug,
            Status: "PENDING",
            CreatedAt: NOW.UtcDateTime.AddDays(-1),
            CreatedCorrelationId: Guid.NewGuid());

    public static ProducerUserSessionModel NewSession(Guid producerUserId, byte[] refreshTokenHash, DateTime? expiresAt = null, DateTime? revokedAt = null, Guid? replacedById = null, Guid? familyId = null)
        => new(
            Id: Guid.NewGuid(),
            ProducerUserId: producerUserId,
            FamilyId: familyId ?? Guid.NewGuid(),
            RefreshTokenHash: refreshTokenHash,
            ExpiresAt: expiresAt ?? NOW.UtcDateTime.AddDays(6),
            CreatedAt: NOW.UtcDateTime.AddDays(-1),
            CreatedCorrelationId: Guid.NewGuid(),
            RevokedAt: revokedAt,
            ReplacedById: replacedById);
}

/// <summary>Relógio controlado pelo teste: avança só quando o teste manda.</summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow()
        => _now;

    public void Advance(TimeSpan delta)
        => _now = _now.Add(delta);
}
