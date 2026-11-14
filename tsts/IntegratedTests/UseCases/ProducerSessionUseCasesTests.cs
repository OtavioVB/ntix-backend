using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.CreateProducer.Inputs;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

[Collection(PostgreSqlCollection.NAME)]
public sealed class ProducerSessionUseCasesTests
{
    private readonly PostgreSqlFixture _fixture;

    public ProducerSessionUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> RegisterProducerAsync()
    {
        var email = ProducerTestData.NewEmail();

        using var scope = _fixture.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IUseCase<CreateProducerUseCaseInput>>().ExecuteAsync(
            CreateProducerUseCaseInput.Create(ProducerTestData.NewName(), email, ProducerTestData.PHONE, "Ana Souza", ProducerTestData.PASSWORD),
            ProducerTestData.NewAuditableInfo());

        Assert.True(result.IsSuccess);

        return email;
    }

    private async Task<MethodResult<ProducerSessionOutput>> LoginAsync(string email, string password = ProducerTestData.PASSWORD)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput>>()
            .ExecuteAsync(CreateProducerSessionUseCaseInput.Create(email, password), ProducerTestData.NewAuditableInfo());
    }

    private async Task<MethodResult<ProducerSessionOutput>> RefreshAsync(string refreshToken)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput>>()
            .ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(refreshToken), ProducerTestData.NewAuditableInfo());
    }

    private async Task<MethodResult> RevokeAsync(string refreshToken)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<RevokeProducerSessionUseCaseInput>>()
            .ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(refreshToken), ProducerTestData.NewAuditableInfo());
    }

    private Task<int> CountSessionsAsync(Guid producerUserId, bool onlyActive)
        => _fixture.ScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM producers.producer_user_session WHERE id_producer_user = @id {(onlyActive ? "AND revoked_at IS NULL" : "")};",
            new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = producerUserId });

    [Fact]
    public async Task Given_RegisteredAdministrator_When_LoggingIn_Should_PersistOneSessionWithTheHashOfTheToken()
    {
        // Arrange
        var email = await RegisterProducerAsync();

        // Act
        var result = await LoginAsync(email.ToUpperInvariant());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("PENDING", result.Output!.User.Status);
        Assert.Equal(1, await CountSessionsAsync(result.Output.User.Id, onlyActive: true));

        var storedHash = await _fixture.ScalarAsync<byte[]>(
            "SELECT refresh_token_hash FROM producers.producer_user_session WHERE id_producer_user = @id;",
            new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = result.Output.User.Id });
        Assert.Equal(RefreshTokenValueObject.Build(result.Output.RefreshToken).Hash, storedHash);
    }

    [Fact]
    public async Task Given_WrongPassword_When_LoggingIn_Should_NotPersistSession()
    {
        // Arrange
        var email = await RegisterProducerAsync();
        var user = await _fixture.FindProducerUserByEmailAsync(email);

        // Act
        var result = await LoginAsync(email, "Errada@123");

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Equal(0, await CountSessionsAsync(user!.Id, onlyActive: false));
    }

    [Fact]
    public async Task Given_LoggedIn_When_RefreshingTwiceAndReusingTheFirstToken_Should_RevokeTheWholeFamily()
    {
        // Arrange
        var login = (await LoginAsync(await RegisterProducerAsync())).Output!;
        var first = await RefreshAsync(login.RefreshToken);
        var second = await RefreshAsync(first.Output!.RefreshToken);

        // Act
        var reuse = await RefreshAsync(login.RefreshToken);

        // Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(reuse.Notifications).Code);
        Assert.Equal(3, await CountSessionsAsync(login.User.Id, onlyActive: false));
        Assert.Equal(0, await CountSessionsAsync(login.User.Id, onlyActive: true));
        Assert.True((await RefreshAsync(second.Output!.RefreshToken)).IsError);
    }

    [Fact]
    public async Task Given_TwoRefreshesWithTheSameTokenAtOnce_When_Running_Should_RotateOnlyOnce()
    {
        // Arrange
        var login = (await LoginAsync(await RegisterProducerAsync())).Output!;

        // Act
        var results = await Task.WhenAll(RefreshAsync(login.RefreshToken), RefreshAsync(login.RefreshToken));

        // Assert: a segunda espera a trava da primeira e encontra o token já substituído, o que é reuso.
        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsError);
        Assert.Equal(2, await CountSessionsAsync(login.User.Id, onlyActive: false));
        Assert.Equal(0, await CountSessionsAsync(login.User.Id, onlyActive: true));
    }

    [Fact]
    public async Task Given_LoggedInTwice_When_LoggingOutOfOne_Should_KeepTheOtherLogin()
    {
        // Arrange
        var email = await RegisterProducerAsync();
        var phone = (await LoginAsync(email)).Output!;
        var laptop = (await LoginAsync(email)).Output!;
        var renewed = (await RefreshAsync(laptop.RefreshToken)).Output!;

        // Act
        var result = await RevokeAsync(renewed.RefreshToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True((await RefreshAsync(renewed.RefreshToken)).IsError);
        Assert.True((await RefreshAsync(phone.RefreshToken)).IsSuccess);
        Assert.True((await RevokeAsync(renewed.RefreshToken)).IsSuccess);
    }

    [Fact]
    public async Task Given_UserBlockedAfterLogin_When_Refreshing_Should_RejectAsBlocked()
    {
        // Arrange
        var login = (await LoginAsync(await RegisterProducerAsync())).Output!;
        await _fixture.ExecuteAsync(
            "UPDATE producers.producer_user SET status = 'BLOCKED' WHERE id_producer_user = @id;",
            new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = login.User.Id });

        // Act
        var result = await RefreshAsync(login.RefreshToken);

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Equal(1, await CountSessionsAsync(login.User.Id, onlyActive: true));
    }
}
