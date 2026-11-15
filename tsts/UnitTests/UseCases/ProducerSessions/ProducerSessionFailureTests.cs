using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics;
using UnitTests.Support;
using static UnitTests.Support.ProducerSessionTestData;

namespace UnitTests.UseCases.ProducerSessions;

/// <summary>Falhas, cancelamentos e spans dos casos de uso da sessão do produtor (REQ5).</summary>
public sealed class ProducerSessionFailureTests : IDisposable
{
    private readonly IProducerUserRepository _producerUserRepository = Substitute.For<IProducerUserRepository>();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IProducerUserSessionRepository _sessionRepository = Substitute.For<IProducerUserSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(NOW);
    private readonly CapturingLogger<CreateProducerSessionUseCase> _createLogger = new();
    private readonly CapturingLogger<RefreshProducerSessionUseCase> _refreshLogger = new();
    private readonly CapturingLogger<RevokeProducerSessionUseCase> _revokeLogger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly ActivityCollector _activities = new(CreateProducerSessionDiagnostics.NAME);

    private readonly string _correlationId = Guid.NewGuid().ToString();
    private readonly RefreshTokenValueObject _refreshToken = RefreshTokenValueObject.Generate();
    private readonly ProducerUserModel _user = NewUser();

    public ProducerSessionFailureTests()
    {
        _producerUserRepository.GetByEmailAsync(EMAIL, Arg.Any<CancellationToken>()).Returns(_user);
        _producerUserRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns(NewProducer());
        _sessionRepository
            .GetByRefreshTokenHashAsync(Arg.Is<byte[]>(hash => hash.SequenceEqual(_refreshToken.Hash)), Arg.Any<CancellationToken>())
            .Returns(NewSession(_user.Id, _refreshToken.Hash));
    }

    public void Dispose()
    {
        _activities.Dispose();
        _meterFactory.Dispose();
    }

    private AuditableInfoValueObject AuditableInfo
        => BuildAuditableInfo(_correlationId);

    private CreateProducerSessionUseCase BuildCreateUseCase()
        => new(
            producerUserRepository: _producerUserRepository,
            producerRepository: _producerRepository,
            sessionRepository: _sessionRepository,
            unitOfWork: _unitOfWork,
            loginAttemptLimiter: new LoginAttemptLimiter(new LoginAttemptConfiguration(), _timeProvider),
            accessTokenIssuer: new ProducerAccessTokenIssuer(JWT_CONFIGURATION),
            jwtConfiguration: JWT_CONFIGURATION,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            timeProvider: _timeProvider,
            diagnostics: new CreateProducerSessionDiagnostics(_meterFactory),
            passwordDiagnostics: new CreateProducerDiagnostics(_meterFactory),
            logger: _createLogger);

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

    private void GivenRollbackFails()
        => _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("rollback"));

    #region Spans

    [Fact]
    public async Task Given_AcceptedLogin_When_Tracing_Should_TagTheOutcomeTheUserAndTheHashIterations()
    {
        // Act
        await BuildCreateUseCase().ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), AuditableInfo);

        // Assert
        var span = _activities.Single("CreateProducerSession", _correlationId);
        Assert.Equal(CreateProducerSessionDiagnostics.OUTCOME_CREATED, span.GetTagItem("ntix.outcome"));
        Assert.Equal(_user.Id, span.GetTagItem("ntix.producer_user_id"));
        Assert.DoesNotContain(span.Tags, tag => tag.Value?.Contains(EMAIL, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Given_LoginFailure_When_Tracing_Should_MarkTheSpanWithTheExceptionTypeOnly()
    {
        // Arrange
        _sessionRepository.CreateAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("dado sensível"));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildCreateUseCase().ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), AuditableInfo));

        // Assert
        var span = _activities.Single("CreateProducerSession", _correlationId);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(nameof(InvalidOperationException), span.StatusDescription);
        Assert.Equal(CreateProducerSessionDiagnostics.OUTCOME_ERROR, span.GetTagItem("ntix.outcome"));
    }

    [Fact]
    public async Task Given_RefreshFailure_When_Tracing_Should_MarkTheSpanAsError()
    {
        // Arrange
        _sessionRepository
            .ReplaceAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha"));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo));

        // Assert
        var span = _activities.Single("RefreshProducerSession", _correlationId);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(RefreshProducerSessionDiagnostics.OUTCOME_ERROR, span.GetTagItem("ntix.outcome"));
        Assert.Contains(_refreshLogger.Logs, log => log.EventId == 5206 && log.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Given_LogoutFailure_When_Tracing_Should_MarkTheSpanAsErrorAndRethrow()
    {
        // Arrange
        _sessionRepository.RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo));

        // Assert
        var span = _activities.Single("RevokeProducerSession", _correlationId);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(RevokeProducerSessionDiagnostics.OUTCOME_ERROR, span.GetTagItem("ntix.outcome"));
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_revokeLogger.Logs, log => log.EventId == 5304 && log.Level == LogLevel.Error);
    }

    #endregion

    #region Rollback que falha não esconde a falha original

    [Fact]
    public async Task Given_RollbackAlsoFails_When_LoginFails_Should_LogBothAndRethrowTheOriginal()
    {
        // Arrange
        _sessionRepository.CreateAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("original"));
        // O primeiro rollback encerra a leitura antes do hash; só o do catch falha.
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask, Task.FromException(new InvalidOperationException("rollback")));

        // Act + Assert
        await Assert.ThrowsAsync<TimeoutException>(() => BuildCreateUseCase().ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), AuditableInfo));
        Assert.Contains(_createLogger.Logs, log => log.EventId == 5106);
        Assert.Contains(_createLogger.Logs, log => log.EventId == 5105);
    }

    [Fact]
    public async Task Given_RollbackAlsoFails_When_RefreshFails_Should_LogBothAndRethrowTheOriginal()
    {
        // Arrange
        _sessionRepository
            .ReplaceAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("original"));
        GivenRollbackFails();

        // Act + Assert
        await Assert.ThrowsAsync<TimeoutException>(() => BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo));
        Assert.Contains(_refreshLogger.Logs, log => log.EventId == 5207);
    }

    [Fact]
    public async Task Given_RollbackAlsoFails_When_LogoutFails_Should_LogBothAndRethrowTheOriginal()
    {
        // Arrange
        _sessionRepository.RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("original"));
        GivenRollbackFails();

        // Act + Assert
        await Assert.ThrowsAsync<TimeoutException>(() => BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo));
        Assert.Contains(_revokeLogger.Logs, log => log.EventId == 5305);
    }

    #endregion

    #region Cancelamento

    [Fact]
    public async Task Given_CanceledRequest_When_Refreshing_Should_ReturnCanceledAfterRollback()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo);

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        Assert.DoesNotContain(_refreshLogger.Logs, log => log.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Given_TokenCanceledWithOtherException_When_Refreshing_Should_StillReturnCanceled()
    {
        // Arrange: o driver pode traduzir o cancelamento em outra exceção; o token cancelado é o que vale.
        using var cancellation = new CancellationTokenSource();
        _sessionRepository
            .GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return Task.FromException<ProducerUserSessionModel?>(new InvalidOperationException("conexão fechada")); });

        // Act
        var result = await BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo, cancellation.Token);

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_CanceledRequest_When_LoggingOut_Should_ReturnCanceledAfterRollback()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await BuildRevokeUseCase().ExecuteAsync(RevokeProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo);

        // Assert
        Assert.Equal(RevokeProducerSessionUseCase.REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Dados que sumiram entre o login e a renovação

    [Fact]
    public async Task Given_UserRemovedAfterLogin_When_Refreshing_Should_RejectAsInvalidToken()
    {
        // Arrange
        _producerUserRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((ProducerUserModel?)null);

        // Act
        var result = await BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo);

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _producerRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ProducerRemovedAfterLogin_When_Refreshing_Should_RejectAsInvalidToken()
    {
        // Arrange
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns((ProducerModel?)null);

        // Act
        var result = await BuildRefreshUseCase().ExecuteAsync(RefreshProducerSessionUseCaseInput.Create(_refreshToken.Value), AuditableInfo);

        // Assert
        Assert.Equal(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().ReplaceAsync(default!, default!, default);
    }

    [Fact]
    public async Task Given_UserWhoseProducerIsGone_When_LoggingIn_Should_RejectAsInvalidCredentials()
    {
        // Arrange
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns((ProducerModel?)null);

        // Act
        var result = await BuildCreateUseCase().ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), AuditableInfo);

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Given_StoredSaltOutOfSize_When_LoggingIn_Should_RejectEvenWithTheRightPassword()
    {
        // Arrange: salt corrompido no banco nunca confere, nem com a senha certa.
        _producerUserRepository.GetByEmailAsync(EMAIL, Arg.Any<CancellationToken>()).Returns(_user with { PasswordSalt = new byte[3] });

        // Act
        var result = await BuildCreateUseCase().ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), AuditableInfo);

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    #endregion
}
