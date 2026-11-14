using Domain.BoundedContexts.ProducerContext.Entities;
using Domain.BoundedContexts.ProducerContext.Enums;
using Domain.ValueObjects;

namespace UnitTests.Producers;

public sealed class ProducerUserSessionTests
{
    private static readonly DateTime NOW = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan LIFETIME = TimeSpan.FromDays(7);

    private static ProducerUserSession StartSession(RefreshTokenValueObject? refreshToken = null)
        => ProducerUserSession.Start(
            producerUserId: IdValueObject.Build(),
            refreshToken: refreshToken ?? RefreshTokenValueObject.Generate(),
            now: NOW,
            lifetime: LIFETIME,
            correlationId: IdValueObject.Build());

    [Fact]
    public void Given_Login_When_StartingSession_Should_StoreOnlyTheHashAndExpireIn7Days()
    {
        // Arrange
        var refreshToken = RefreshTokenValueObject.Generate();

        // Act
        var session = StartSession(refreshToken);

        // Assert
        Assert.Equal(refreshToken.Hash, session.RefreshTokenHash);
        Assert.Equal(NOW.AddDays(7), session.ExpiresAt.Value);
        Assert.Equal(ProducerUserSessionState.ACTIVE, session.GetState(NOW));
    }

    [Fact]
    public void Given_TwoLogins_When_StartingSessions_Should_OpenDifferentFamilies()
    {
        // Act
        var first = StartSession();
        var second = StartSession();

        // Assert
        Assert.NotEqual(first.FamilyId.Value, second.FamilyId.Value);
    }

    [Fact]
    public void Given_ActiveSession_When_Rotating_Should_RevokeAndReplaceItInTheSameFamily()
    {
        // Arrange
        var session = StartSession();
        var later = NOW.AddHours(1);
        var nextToken = RefreshTokenValueObject.Generate();

        // Act
        var (replaced, next) = session.Rotate(nextToken, later, LIFETIME, IdValueObject.Build());

        // Assert
        Assert.Equal(session.Id.Value, replaced.Id.Value);
        Assert.Equal(later, replaced.RevokedAt!.Value.Value);
        Assert.Equal(next.Id.Value, replaced.ReplacedById!.Value.Value);
        Assert.Equal(ProducerUserSessionState.REPLACED, replaced.GetState(later));

        Assert.Equal(session.FamilyId.Value, next.FamilyId.Value);
        Assert.Equal(session.ProducerUserId.Value, next.ProducerUserId.Value);
        Assert.Equal(nextToken.Hash, next.RefreshTokenHash);
        Assert.Equal(later.AddDays(7), next.ExpiresAt.Value);
        Assert.Equal(ProducerUserSessionState.ACTIVE, next.GetState(later));
    }

    [Fact]
    public void Given_SessionPast7Days_When_GettingState_Should_BeExpired()
    {
        // Arrange
        var session = StartSession();

        // Act
        var justBefore = session.GetState(NOW.AddDays(7).AddTicks(-1));
        var atExpiration = session.GetState(NOW.AddDays(7));

        // Assert
        Assert.Equal(ProducerUserSessionState.ACTIVE, justBefore);
        Assert.Equal(ProducerUserSessionState.EXPIRED, atExpiration);
    }

    [Fact]
    public void Given_RevokedSessionNotReplaced_When_GettingState_Should_BeRevoked()
    {
        // Arrange
        var session = StartSession() with { RevokedAt = DateTimeValueObject.Build(NOW) };

        // Act
        var state = session.GetState(NOW);

        // Assert
        Assert.Equal(ProducerUserSessionState.REVOKED, state);
    }
}
