using Application.UseCases.Base.Authentication;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;
using static UnitTests.Support.ProducerSessionTestData;

namespace UnitTests.UseCases.ProducerSessions;

public sealed class RefreshAndRevokeProducerSessionUseCasesTests : IDisposable
{
    private readonly IProducerUserSessionRepository _sessionRepository = Substitute.For<IProducerUserSessionRepository>();
    private readonly IProducerUserRepository _producerUserRepository = Substitute.For<IProducerUserRepository>();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(NOW);
    private readonly CapturingLogger<RefreshProducerSessionUseCase> _refreshLogger = new();
    private readonly CapturingLogger<RevokeProducerSessionUseCase> _revokeLogger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;

    private readonly RefreshTokenValueObject _refreshToken = RefreshTokenValueObject.Generate();
    private readonly ProducerUserModel _user = NewUser();

    private (ProducerUserSessionModel Replaced, ProducerUserSessionModel Next)? _replacement;

    public RefreshAndRevokeProducerSessionUseCasesTests()
    {
        _producerUserRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns(NewProducer());
        _sessionRepository
            .ReplaceAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _replacement = (call.ArgAt<ProducerUserSessionModel>(0), call.ArgAt<ProducerUserSessionModel>(1)));

        _measurements = new MeasurementCollector(_meterFactory);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private RefreshProducerSessionUseCase BuildRefreshUseCase()
        => new(
            sessionRepository: _sessionRepository,
            producerUserRepository: _producerUserRepository,
            producerRepository: _producerRepository,
            unitOfWork: _unitOfWork,
            accessTokenIssuer: new ProducerAccessTokenIssuer(JWT_CONFIGURATION),
            jwtConfiguration: JWT_CONFIGURATION,
            timeProvider: _timeProvider,
            diagnostics: new RefreshProducerSessionDiagnostics(_meterFactory),
            logger: _refreshLogger);

    private RevokeProducerSessionUseCase BuildRevokeUseCase()
        => new(
            sessionRepository: _sessionRepository,
            unitOfWork: _unitOfWork,
            timeProvider: _timeProvider,
            diagnostics: new RevokeProducerSessionDiagnostics(_meterFactory),
            logger: _revokeLogger);

    private ProducerUserSessionModel GivenStoredSession(DateTime? expiresAt = null, DateTime? revokedAt = null, Guid? replacedById = null)
    {
        var session = NewSession(_user.Id, _refreshToken.Hash, expiresAt, revokedAt, replacedById);

        _sessionRepository
            .GetByRefreshTokenHashAsync(Arg.Is<byte[]>(hash => hash.SequenceEqual(_refreshToken.Hash)), Arg.Any<CancellationToken>())
            .Returns(session);

        return session;
    }

    private Task<Domain.CrossCutting.MethodResultContext.MethodResult<Application.UseCases.Base.Producers.Outputs.ProducerSessionOutput>> RefreshAsync(string? token = null)
        => BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(token ?? _refreshToken.Value), BuildAuditableInfo());

    private bool HasRefreshOutcome(string outcome)
        => _measurements.Measurements.Any(measurement => measurement.Instrument == "ntix.producers.sessions.refresh.outcomes" && measurement.Value == 1 && measurement.Outcome == outcome);

    [Fact]
    public async Task Given_ActiveSession_When_Refreshing_Should_ReplaceItByANewOneOfTheSameFamily()
    {
        // Arrange
        var stored = GivenStoredSession();

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(_refreshToken.Value, result.Output!.RefreshToken);

        var (replaced, next) = _replacement!.Value;
        Assert.Equal(stored.Id, replaced.Id);
        Assert.Equal(NOW.UtcDateTime, replaced.RevokedAt);
        Assert.Equal(next.Id, replaced.ReplacedById);
        Assert.Equal(stored.FamilyId, next.FamilyId);
        Assert.Equal(RefreshTokenValueObject.Build(result.Output.RefreshToken).Hash, next.RefreshTokenHash);
        Assert.Equal(NOW.UtcDateTime.AddDays(7), next.ExpiresAt);

        Assert.Equal(next.Id.ToString(), new JsonWebToken(result.Output.AccessToken).Id);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.True(HasRefreshOutcome(RefreshProducerSessionDiagnostics.OUTCOME_REFRESHED));
        Assert.Contains(_refreshLogger.Logs, log => log.EventId == 5201);
    }

    [Fact]
    public async Task Given_ProducerSlugChangedSinceLogin_When_Refreshing_Should_IssueTokenWithTheCurrentSlug()
    {
        // Arrange
        GivenStoredSession();
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns(NewProducer(slug: "produtora-nova"));

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal("produtora-nova", new JsonWebToken(result.Output!.AccessToken).GetClaim(ProducerClaims.PRODUCER_SLUG).Value);
        Assert.Equal("produtora-nova", result.Output.Producer.Slug);
    }

    [Fact]
    public async Task Given_TokenAlreadyReplaced_When_PresentedAgain_Should_RevokeTheWholeFamily()
    {
        // Arrange
        var stored = GivenStoredSession(revokedAt: NOW.UtcDateTime.AddMinutes(-10), replacedById: Guid.NewGuid());
        _sessionRepository.RevokeFamilyAsync(stored.FamilyId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.Received(1).RevokeFamilyAsync(stored.FamilyId, NOW.UtcDateTime, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _sessionRepository.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default);
        Assert.True(HasRefreshOutcome(RefreshProducerSessionDiagnostics.OUTCOME_REUSE_DETECTED));
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.sessions.refresh.reuse" && measurement.Value == 1);
        Assert.Contains(_refreshLogger.Logs, log => log.EventId == 5203 && log.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task Given_RevokedSession_When_Refreshing_Should_RejectWithoutChanges()
    {
        // Arrange
        GivenStoredSession(revokedAt: NOW.UtcDateTime.AddMinutes(-1));

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ExpiredSession_When_Refreshing_Should_Reject()
    {
        // Arrange
        GivenStoredSession(expiresAt: NOW.UtcDateTime);

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default);
        Assert.True(HasRefreshOutcome(RefreshProducerSessionDiagnostics.OUTCOME_INVALID_TOKEN));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Given_UnknownOrMalformedToken_When_Refreshing_Should_RejectAsInvalidToken(bool wellFormed)
    {
        // Act
        var result = await RefreshAsync(wellFormed ? RefreshTokenValueObject.Generate().Value : "qualquer-coisa");

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UserBlockedAfterLogin_When_Refreshing_Should_RejectAsBlocked()
    {
        // Arrange
        GivenStoredSession();
        _producerUserRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user with { Status = "BLOCKED" });

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.True(HasRefreshOutcome(RefreshProducerSessionDiagnostics.OUTCOME_BLOCKED));
    }

    [Fact]
    public async Task Given_MissingAuditableHeaders_When_Refreshing_Should_ReturnValidation()
    {
        // Act
        var result = await BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        Assert.DoesNotContain(result.Notifications, notification => notification.Code == RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_DatabaseFailure_When_Refreshing_Should_RollbackAndRethrow()
    {
        // Arrange
        GivenStoredSession();
        _sessionRepository
            .ReplaceAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha"));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => RefreshAsync());
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ActiveSession_When_LoggingOut_Should_RevokeTheWholeFamily()
    {
        // Arrange
        var stored = GivenStoredSession();
        _sessionRepository.RevokeFamilyAsync(stored.FamilyId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2);

        // Act
        var result = await BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(_refreshToken.Value), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _sessionRepository.Received(1).RevokeFamilyAsync(stored.FamilyId, NOW.UtcDateTime, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_revokeLogger.Logs, log => log.EventId == 5301 && log.Message.Contains(stored.FamilyId.ToString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Given_UnknownOrMalformedToken_When_LoggingOut_Should_SucceedWithoutChanges(bool wellFormed)
    {
        // Arrange
        var token = wellFormed ? RefreshTokenValueObject.Generate().Value : "qualquer-coisa";

        // Act
        var result = await BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(token), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _sessionRepository.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_revokeLogger.Logs, log => log.EventId == 5302);
    }

    [Fact]
    public async Task Given_MissingAuditableHeaders_When_LoggingOut_Should_ReturnValidation()
    {
        // Act
        var result = await BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfoValueObject.Build(null, null, null, null));

        // Assert
        Assert.True(result.IsError);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }
}
