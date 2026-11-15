using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;
using static UnitTests.Support.ProducerSessionTestData;

namespace UnitTests.UseCases.ProducerSessions;

public sealed class CreateProducerSessionUseCaseTests : IDisposable
{
    private const string PASSWORD_HASH_DURATION = "ntix.producers.password.hash.duration";

    private readonly IProducerUserRepository _producerUserRepository = Substitute.For<IProducerUserRepository>();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IProducerUserSessionRepository _sessionRepository = Substitute.For<IProducerUserSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _timeProvider = new(NOW);
    private readonly CapturingLogger<CreateProducerSessionUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;

    private ProducerUserSessionModel? _createdSession;

    public CreateProducerSessionUseCaseTests()
    {
        _producerRepository.GetByIdAsync(PRODUCER_ID, Arg.Any<CancellationToken>()).Returns(NewProducer());
        _sessionRepository
            .CreateAsync(Arg.Do<ProducerUserSessionModel>(session => _createdSession = session), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _measurements = new MeasurementCollector(_meterFactory);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private CreateProducerSessionUseCase BuildUseCase(int permitLimit = 10)
        => new(
            producerUserRepository: _producerUserRepository,
            producerRepository: _producerRepository,
            sessionRepository: _sessionRepository,
            unitOfWork: _unitOfWork,
            loginAttemptLimiter: new LoginAttemptLimiter(new LoginAttemptConfiguration(PermitLimit: permitLimit), _timeProvider),
            accessTokenIssuer: new ProducerAccessTokenIssuer(JWT_CONFIGURATION),
            jwtConfiguration: JWT_CONFIGURATION,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            timeProvider: _timeProvider,
            diagnostics: new CreateProducerSessionDiagnostics(_meterFactory),
            passwordDiagnostics: new CreateProducerDiagnostics(_meterFactory),
            logger: _logger);

    private void GivenUser(ProducerUserModel? user)
        => _producerUserRepository.GetByEmailAsync(EMAIL, Arg.Any<CancellationToken>()).Returns(user);

    private Task<Domain.CrossCutting.MethodResultContext.MethodResult<Application.UseCases.Base.Producers.Outputs.ProducerSessionOutput>> LoginAsync(string? email = EMAIL, string? password = PASSWORD, int permitLimit = 10)
        => BuildUseCase(permitLimit).ExecuteAsync(CreateProducerSessionUseCaseInput.Create(email, password), BuildAuditableInfo());

    private bool HasOutcome(string outcome)
        => _measurements.Measurements.Any(measurement => measurement.Instrument == "ntix.producers.sessions.creation.outcomes" && measurement.Value == 1 && measurement.Outcome == outcome);

    [Theory]
    [InlineData("PENDING")]
    [InlineData("ACTIVE")]
    public async Task Given_PendingOrActiveUserWithRightPassword_When_LoggingIn_Should_OpenSessionWithBothTokens(string status)
    {
        // Arrange
        var user = NewUser(status);
        GivenUser(user);

        // Act
        var result = await LoginAsync(email: "  ANA@Produtora.com.br ");

        // Assert
        Assert.True(result.IsSuccess);
        var output = result.Output!;

        Assert.Equal(900, output.AccessTokenExpiresIn);
        Assert.Equal(604800, output.RefreshTokenExpiresIn);
        Assert.Equal(43, output.RefreshToken.Length);
        Assert.Equal(user.Id, output.User.Id);
        Assert.Equal(status, output.User.Status);
        Assert.Equal(PRODUCER_SLUG, output.Producer.Slug);

        var jwt = new JsonWebToken(output.AccessToken);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(_createdSession!.Id.ToString(), jwt.Id);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_CREATED));
        Assert.Contains(_logger.Logs, log => log.EventId == 5101 && log.Message.Contains(PRODUCER_SLUG));
    }

    [Fact]
    public async Task Given_Login_When_OpeningSession_Should_StoreOnlyTheSha256OfTheRefreshToken()
    {
        // Arrange
        GivenUser(NewUser());

        // Act
        var result = await LoginAsync();

        // Assert
        var session = _createdSession!;
        Assert.Equal(RefreshTokenValueObject.Build(result.Output!.RefreshToken).Hash, session.RefreshTokenHash);
        Assert.Equal(NOW.UtcDateTime.AddDays(7), session.ExpiresAt);
        Assert.Null(session.RevokedAt);
        Assert.Equal(Guid.Parse(CORRELATION_ID), session.CreatedCorrelationId);
    }

    [Fact]
    public async Task Given_WrongPassword_When_LoggingIn_Should_RejectAsInvalidCredentialsWithoutSession()
    {
        // Arrange
        GivenUser(NewUser());

        // Act
        var result = await LoginAsync(password: "Errada@123");

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_INVALID_CREDENTIALS));

        var log = Assert.Single(_logger.Logs, log => log.EventId == 5102);
        Assert.Contains("INVALID_CREDENTIALS", log.Message);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        Assert.DoesNotContain(EMAIL, log.Message);
    }

    [Fact]
    public async Task Given_UnknownEmail_When_LoggingIn_Should_HashWithFixedSaltAndAnswerLikeWrongPassword()
    {
        // Arrange
        GivenUser(null);

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.Contains(_measurements.Durations, duration => duration.Instrument == PASSWORD_HASH_DURATION);
        await _producerRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _sessionRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Given_BlockedUserWithRightPassword_When_LoggingIn_Should_RejectAsBlocked()
    {
        // Arrange
        GivenUser(NewUser("BLOCKED"));

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_BLOCKED));
        Assert.Contains(_logger.Logs, log => log.EventId == 5102 && log.Message.Contains("BLOCKED"));
    }

    [Fact]
    public async Task Given_BlockedUserWithWrongPassword_When_LoggingIn_Should_NotRevealTheBlock()
    {
        // Arrange
        GivenUser(NewUser("BLOCKED"));

        // Act
        var result = await LoginAsync(password: "Errada@123");

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_StrengthRuleViolation_When_LoggingIn_Should_OnlyCheckThePassword()
    {
        // Arrange: a regra de força é do cadastro; no login, "senha" é só uma senha errada.
        GivenUser(NewUser());

        // Act
        var result = await LoginAsync(password: "senha");

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Theory]
    [InlineData("não-é-email", PASSWORD, "EMAIL_FORMAT_INVALID")]
    [InlineData(EMAIL, "", "PASSWORD_COULD_NOT_BE_EMPTY")]
    [InlineData(EMAIL, null, "PASSWORD_COULD_NOT_BE_EMPTY")]
    public async Task Given_InvalidData_When_LoggingIn_Should_ReturnValidationWithoutTouchingTheDatabase(string email, string? password, string expectedCode)
    {
        // Act
        var result = await LoginAsync(email, password);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == expectedCode);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_VALIDATION_FAILED));
    }

    [Fact]
    public async Task Given_AttemptLimitReached_When_LoggingIn_Should_RejectWithoutHashNorDatabase()
    {
        // Arrange
        GivenUser(NewUser());
        var useCase = BuildUseCase(permitLimit: 1);
        await useCase.ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, "Errada@123"), BuildAuditableInfo());
        _unitOfWork.ClearReceivedCalls();
        _measurements.Durations.Clear();

        // Act
        var result = await useCase.ExecuteAsync(CreateProducerSessionUseCaseInput.Create(EMAIL, PASSWORD), BuildAuditableInfo());

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        Assert.DoesNotContain(_measurements.Durations, duration => duration.Instrument == PASSWORD_HASH_DURATION);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_RATE_LIMITED));
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.sessions.login.rate_limited" && measurement.Value == 1);
        Assert.Contains(_logger.Logs, log => log.EventId == 5103 && log.Message.Contains(OBFUSCATED_EMAIL));
    }

    [Fact]
    public async Task Given_DatabaseFailure_When_OpeningSession_Should_RollbackAndRethrow()
    {
        // Arrange
        GivenUser(NewUser());
        _sessionRepository.CreateAsync(Arg.Any<ProducerUserSessionModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => LoginAsync());

        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
        Assert.True(HasOutcome(CreateProducerSessionDiagnostics.OUTCOME_ERROR));
        Assert.Contains(_logger.Logs, log => log.EventId == 5105 && log.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Given_CanceledRequest_When_LoggingIn_Should_ReturnCanceledNotification()
    {
        // Arrange
        _producerUserRepository.GetByEmailAsync(EMAIL, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Equal(CreateProducerSessionUseCase.CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received().RollbackAsync(Arg.Any<CancellationToken>());
    }
}
