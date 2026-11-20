using Domain.BoundedContexts.CustomerContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.CustomerContext.Entities;

/// <summary>
/// Sessão do cliente (REQ12): um refresh token, guardado só pelo hash. Cada renovação revoga o token usado e abre outra
/// sessão da mesma família (<see cref="FamilyId"/>), como a sessão do produtor (REQ5).
/// </summary>
public sealed record CustomerSession
{
    public CustomerSession(
        IdValueObject id,
        IdValueObject customerId,
        IdValueObject familyId,
        byte[] refreshTokenHash,
        DateTimeValueObject expiresAt,
        DateTimeValueObject createdAt,
        IdValueObject createdCorrelationId,
        DateTimeValueObject? revokedAt,
        IdValueObject? replacedById)
    {
        Id = id;
        CustomerId = customerId;
        FamilyId = familyId;
        RefreshTokenHash = refreshTokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        RevokedAt = revokedAt;
        ReplacedById = replacedById;
    }

    public IdValueObject Id { get; init; }
    public IdValueObject CustomerId { get; init; }
    public IdValueObject FamilyId { get; init; }

    /// <summary>SHA-256 do refresh token (RN4). O token em claro nunca é gravado.</summary>
    public byte[] RefreshTokenHash { get; init; }

    public DateTimeValueObject ExpiresAt { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject? RevokedAt { get; init; }
    public IdValueObject? ReplacedById { get; init; }

    /// <summary>Sessão de um login com senha: abre uma família nova.</summary>
    public static CustomerSession Start(IdValueObject customerId, RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
        => Open(customerId, IdValueObject.Build(), refreshToken, now, lifetime, correlationId);

    /// <summary>Renovação (RN5): esta sessão revogada e substituída, e a próxima, da mesma família, com validade nova.</summary>
    public (CustomerSession Replaced, CustomerSession Next) Rotate(RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
    {
        var next = Open(CustomerId, FamilyId, refreshToken, now, lifetime, correlationId);

        return (this with { RevokedAt = DateTimeValueObject.Build(now), ReplacedById = next.Id }, next);
    }

    public CustomerSessionState GetState(DateTime now)
    {
        if (ReplacedById is not null)
            return CustomerSessionState.REPLACED;

        if (RevokedAt is not null)
            return CustomerSessionState.REVOKED;

        return now >= ExpiresAt.Value ? CustomerSessionState.EXPIRED : CustomerSessionState.ACTIVE;
    }

    private static CustomerSession Open(IdValueObject customerId, IdValueObject familyId, RefreshTokenValueObject refreshToken, DateTime now, TimeSpan lifetime, IdValueObject correlationId)
        => new(
            id: IdValueObject.Build(),
            customerId: customerId,
            familyId: familyId,
            refreshTokenHash: refreshToken.Hash,
            expiresAt: DateTimeValueObject.Build(now.Add(lifetime)),
            createdAt: DateTimeValueObject.Build(now),
            createdCorrelationId: correlationId,
            revokedAt: null,
            replacedById: null);
}
