using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using System.Security.Cryptography;

namespace IntegratedTests.Repositories;

[Collection(PostgreSqlCollection.NAME)]
public sealed class ProducerUserSessionRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public ProducerUserSessionRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<T> InTransactionAsync<T>(Func<IProducerUserSessionRepository, Task<T>> action, bool commit = true)
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IProducerUserSessionRepository>();

        await unitOfWork.BeginTransactionAsync();

        var result = await action(repository);

        if (commit)
            await unitOfWork.CommitAsync();
        else
            await unitOfWork.RollbackAsync();

        return result;
    }

    private async Task<Guid> CreateProducerUserAsync()
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IProducerRepository>();
        var producer = ProducerTestData.NewModel();
        var user = ProducerTestData.NewUserModel(producer.Id, producer.Email);

        await unitOfWork.BeginTransactionAsync();
        await repository.CreateAsync(producer);
        await repository.CreateUserAsync(user);
        await unitOfWork.CommitAsync();

        return user.Id;
    }

    private static ProducerUserSessionModel NewSession(Guid producerUserId, Guid? familyId = null)
    {
        var now = DateTime.UtcNow;

        return new ProducerUserSessionModel(
            Id: Guid.NewGuid(),
            ProducerUserId: producerUserId,
            FamilyId: familyId ?? Guid.NewGuid(),
            RefreshTokenHash: RandomNumberGenerator.GetBytes(32),
            ExpiresAt: now.AddDays(7),
            CreatedAt: now,
            CreatedCorrelationId: Guid.NewGuid(),
            RevokedAt: null,
            ReplacedById: null);
    }

    private Task<int> CountActiveInFamilyAsync(Guid familyId)
        => _fixture.ScalarAsync<int>(
            "SELECT COUNT(*)::int FROM producers.producer_user_session WHERE id_family = @id_family AND revoked_at IS NULL;",
            new NpgsqlParameter("@id_family", NpgsqlDbType.Uuid) { Value = familyId });

    [Fact]
    public async Task Given_NewSession_When_Creating_Should_PersistOnlyTheHashAndFindItByHash()
    {
        // Arrange
        var session = NewSession(await CreateProducerUserAsync());

        // Act
        await InTransactionAsync(async repository => { await repository.CreateAsync(session); return 0; });
        var found = await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(session.RefreshTokenHash));

        // Assert
        Assert.NotNull(found);
        Assert.Equal(session.Id, found.Id);
        Assert.Equal(session.RefreshTokenHash, found.RefreshTokenHash);
        Assert.Equal(session.FamilyId, found.FamilyId);
        Assert.Null(found.RevokedAt);
        Assert.Null(found.ReplacedById);

        var columns = await _fixture.ScalarAsync<string>("""
            SELECT string_agg(column_name, ',' ORDER BY column_name)
            FROM information_schema.columns
            WHERE table_schema = 'producers' AND table_name = 'producer_user_session';
            """);
        Assert.DoesNotContain("refresh_token,", columns + ",", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_HashOtherThan32Bytes_When_Creating_Should_BeRefusedByTheDatabase()
    {
        // Arrange
        var session = NewSession(await CreateProducerUserAsync()) with { RefreshTokenHash = RandomNumberGenerator.GetBytes(16) };

        // Act + Assert
        await Assert.ThrowsAsync<PostgresException>(() => InTransactionAsync(async repository => { await repository.CreateAsync(session); return 0; }));
    }

    [Fact]
    public async Task Given_ActiveSession_When_Replacing_Should_InsertTheNextAndMarkTheCurrentAtomically()
    {
        // Arrange
        var userId = await CreateProducerUserAsync();
        var current = NewSession(userId);
        await InTransactionAsync(async repository => { await repository.CreateAsync(current); return 0; });
        var next = NewSession(userId, current.FamilyId);
        var revokedAt = DateTime.UtcNow;

        // Act
        await InTransactionAsync(async repository => { await repository.ReplaceAsync(current with { RevokedAt = revokedAt, ReplacedById = next.Id }, next); return 0; });

        // Assert
        var replaced = await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(current.RefreshTokenHash));
        var inserted = await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(next.RefreshTokenHash));
        Assert.Equal(next.Id, replaced!.ReplacedById);
        Assert.NotNull(replaced.RevokedAt);
        Assert.NotNull(inserted);
        Assert.Null(inserted.RevokedAt);
        Assert.Equal(1, await CountActiveInFamilyAsync(current.FamilyId));
    }

    [Fact]
    public async Task Given_ReplacementRolledBack_When_Reading_Should_KeepTheCurrentActiveAndNoNextSession()
    {
        // Arrange
        var userId = await CreateProducerUserAsync();
        var current = NewSession(userId);
        await InTransactionAsync(async repository => { await repository.CreateAsync(current); return 0; });
        var next = NewSession(userId, current.FamilyId);

        // Act
        await InTransactionAsync(async repository => { await repository.ReplaceAsync(current with { RevokedAt = DateTime.UtcNow, ReplacedById = next.Id }, next); return 0; }, commit: false);

        // Assert
        var stillCurrent = await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(current.RefreshTokenHash));
        Assert.Null(stillCurrent!.RevokedAt);
        Assert.Null(await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(next.RefreshTokenHash)));
    }

    [Fact]
    public async Task Given_FamilyWithSeveralSessions_When_RevokingTheFamily_Should_RevokeOnlyThatFamily()
    {
        // Arrange
        var userId = await CreateProducerUserAsync();
        var family = Guid.NewGuid();
        var first = NewSession(userId, family);
        var second = NewSession(userId, family);
        var otherLogin = NewSession(userId);
        await InTransactionAsync(async repository =>
        {
            await repository.CreateAsync(first);
            await repository.CreateAsync(second);
            await repository.CreateAsync(otherLogin);
            return 0;
        });

        // Act
        var revoked = await InTransactionAsync(repository => repository.RevokeFamilyAsync(family, DateTime.UtcNow));
        var revokedAgain = await InTransactionAsync(repository => repository.RevokeFamilyAsync(family, DateTime.UtcNow));

        // Assert
        Assert.Equal(2, revoked);
        Assert.Equal(0, revokedAgain);
        Assert.Equal(0, await CountActiveInFamilyAsync(family));
        Assert.Equal(1, await CountActiveInFamilyAsync(otherLogin.FamilyId));
    }
}
