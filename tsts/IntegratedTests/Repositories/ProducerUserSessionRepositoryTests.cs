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
    public async Task Given_SessionAlreadyRevoked_When_Replacing_Should_ThrowAndKeepNothing()
    {
        // Arrange: outra transação revogou a sessão depois de ela ser lida, o que o FOR UPDATE impede no caso de uso.
        var userId = await CreateProducerUserAsync();
        var current = NewSession(userId);
        await InTransactionAsync(async repository => { await repository.CreateAsync(current); return 0; });
        await InTransactionAsync(repository => repository.RevokeFamilyAsync(current.FamilyId, DateTime.UtcNow));
        var next = NewSession(userId, current.FamilyId);

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InTransactionAsync(async repository => { await repository.ReplaceAsync(current with { RevokedAt = DateTime.UtcNow, ReplacedById = next.Id }, next); return 0; }));
        Assert.Null(await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(next.RefreshTokenHash)));
    }

    [Fact]
    public async Task Given_ReplacedSessionWithoutRevocationDate_When_Replacing_Should_Throw()
    {
        // Arrange
        var userId = await CreateProducerUserAsync();
        var current = NewSession(userId);
        var next = NewSession(userId, current.FamilyId);

        // Act + Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InTransactionAsync(async repository => { await repository.ReplaceAsync(current, next); return 0; }, commit: false));
    }

    [Fact]
    public async Task Given_UnknownHash_When_Searching_Should_ReturnNull()
    {
        // Act
        var found = await InTransactionAsync(repository => repository.GetByRefreshTokenHashAsync(RandomNumberGenerator.GetBytes(32)));

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task Given_ProducerUser_When_SearchingByEmailAndId_Should_ReturnItAndNullForUnknown()
    {
        // Arrange
        var producer = ProducerTestData.NewModel();
        var user = ProducerTestData.NewUserModel(producer.Id, producer.Email);
        using (var scope = _fixture.Services.CreateScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var producerRepository = scope.ServiceProvider.GetRequiredService<IProducerRepository>();
            await unitOfWork.BeginTransactionAsync();
            await producerRepository.CreateAsync(producer);
            await producerRepository.CreateUserAsync(user);
            await unitOfWork.CommitAsync();
        }

        using var readScope = _fixture.Services.CreateScope();
        var readUnitOfWork = readScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = readScope.ServiceProvider.GetRequiredService<IProducerUserRepository>();
        var producers = readScope.ServiceProvider.GetRequiredService<IProducerRepository>();
        await readUnitOfWork.BeginTransactionAsync();

        // Act
        var byEmail = await users.GetByEmailAsync(user.Email);
        var byId = await users.GetByIdAsync(user.Id);
        var unknownEmail = await users.GetByEmailAsync(ProducerTestData.NewEmail());
        var unknownId = await users.GetByIdAsync(Guid.NewGuid());
        var producerById = await producers.GetByIdAsync(producer.Id);
        var unknownProducer = await producers.GetByIdAsync(Guid.NewGuid());
        await readUnitOfWork.RollbackAsync();

        // Assert
        Assert.Equal(user.Id, byEmail!.Id);
        Assert.Equal(user.PasswordHash, byEmail.PasswordHash);
        Assert.Equal(user.PasswordSalt, byEmail.PasswordSalt);
        Assert.Equal(user.Email, byId!.Email);
        Assert.Null(unknownEmail);
        Assert.Null(unknownId);
        Assert.Equal(producer.Slug, producerById!.Slug);
        Assert.Equal(producer.Status, producerById.Status);
        Assert.Null(unknownProducer);
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
