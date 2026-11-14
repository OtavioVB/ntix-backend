using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.ProducerContext.Entities;

/// <summary>
/// Sessão do usuário do produtor (REQ5): um refresh token, guardado só pelo hash. Cada renovação revoga o token usado e
/// abre outra sessão da mesma família (<see cref="FamilyId"/>), que agrupa todas as renovações de um login.
/// </summary>
public sealed record ProducerUserSession
{
    public ProducerUserSession(
        IdValueObject id,
        IdValueObject producerUserId,
        IdValueObject familyId,
        byte[] refreshTokenHash,
        DateTimeValueObject expiresAt,
        DateTimeValueObject createdAt,
        IdValueObject createdCorrelationId,
        DateTimeValueObject? revokedAt,
        IdValueObject? replacedById)
    {
        Id = id;
        ProducerUserId = producerUserId;
        FamilyId = familyId;
        RefreshTokenHash = refreshTokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        RevokedAt = revokedAt;
        ReplacedById = replacedById;
    }

    public IdValueObject Id { get; init; }
    public IdValueObject ProducerUserId { get; init; }
    public IdValueObject FamilyId { get; init; }

    /// <summary>SHA-256 do refresh token (RN6). O token em claro nunca é gravado.</summary>
    public byte[] RefreshTokenHash { get; init; }

    public DateTimeValueObject ExpiresAt { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject? RevokedAt { get; init; }
    public IdValueObject? ReplacedById { get; init; }

    /// <summary>Sessão de um login com senha: abre uma família nova.</summary>
    public static ProducerUserSession Start(IdValueObject producerUserId, RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
        => Open(producerUserId, IdValueObject.Build(), refreshToken, now, lifetime, correlationId);

    /// <summary>
    /// Renovação (RN7): devolve esta sessão revogada e substituída e a próxima, da mesma família, com o novo token e
    /// uma validade nova.
    /// </summary>
    public (ProducerUserSession Replaced, ProducerUserSession Next) Rotate(RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
    {
        var next = Open(ProducerUserId, FamilyId, refreshToken, now, lifetime, correlationId);

        var replaced = this with
        {
            RevokedAt = DateTimeValueObject.Build(now),
            ReplacedById = next.Id,
        };

        return (replaced, next);
    }

    public ProducerUserSessionState GetState(DateTime now)
    {
        if (ReplacedById is not null)
            return ProducerUserSessionState.REPLACED;

        if (RevokedAt is not null)
            return ProducerUserSessionState.REVOKED;

        return now >= ExpiresAt.Value
            ? ProducerUserSessionState.EXPIRED
            : ProducerUserSessionState.ACTIVE;
    }

    private static ProducerUserSession Open(IdValueObject producerUserId, IdValueObject familyId, RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
        => new(
            id: IdValueObject.Build(),
            producerUserId: producerUserId,
            familyId: familyId,
            refreshTokenHash: refreshToken.Hash,
            expiresAt: DateTimeValueObject.Build(now.Add(lifetime)),
            createdAt: DateTimeValueObject.Build(now),
            createdCorrelationId: correlationId,
            revokedAt: null,
            replacedById: null);
}
