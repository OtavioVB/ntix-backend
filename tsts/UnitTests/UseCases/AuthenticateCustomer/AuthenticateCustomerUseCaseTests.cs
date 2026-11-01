using Application.UseCases.AuthenticateCustomer;
using Application.UseCases.AuthenticateCustomer.Inputs;
using Application.UseCases.Base.Configuration;
using Domain.ValueObjects;
using Infra.Jwt.Base.TokenGenerator.Interfaces;
using Infra.SqlServer.Repositories.Customers.Interfaces;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.AuthenticateCustomer;

public sealed class AuthenticateCustomerUseCaseTests : IDisposable
{
    private const string EMAIL = "maria@ntix.com";
    private const string OBFUSCATED_EMAIL = "ma***@ntix.com";
    private const string DOCUMENT = "52998224725";
    private const string OBFUSCATED_DOCUMENT = "***.982.247-**";
    private const string PASSWORD = "Senha@123";
    private const string WRONG_PASSWORD = "Errada@123";
    private const string PRIVATE_KEY = "chave-de-teste";
    private const string ACCESS_TOKEN = "token-de-teste";
    private const string AUTHENTICATION_FAILED = "AUTHENTICATION_FAILED";

    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "customer-portal";
    private const string EXECUTION_USER = "user-123";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private static readonly Guid CUSTOMER_ID = Guid.NewGuid();
    private static readonly Guid PRODUCER_ID = Guid.NewGuid();

    private readonly ICustomerRepository _repository = Substitute.For<ICustomerRepository>();
    private readonly IJwtTokenGenerator _tokenGenerator = Substitute.For<IJwtTokenGenerator>();
    private readonly CapturingLogger<AuthenticateCustomerUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly AuthenticateCustomerUseCase _useCase;

    public AuthenticateCustomerUseCaseTests()
    {
        _tokenGenerator
            .Generate(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(ACCESS_TOKEN);

        var diagnostics = new AuthenticateCustomerDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new AuthenticateCustomerUseCase(
            customerRepository: _repository,
            jwtTokenGenerator: _tokenGenerator,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            diagnostics: diagnostics,
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static AuthenticateCustomerUseCaseInput BuildInput(string? email = EMAIL, string? document = null, string? password = PASSWORD, string? producerId = null)
        => AuthenticateCustomerUseCaseInput.Create(email, document, password, producerId ?? PRODUCER_ID.ToString());

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

    private static CustomerCredentialsModel BuildCredentials(bool isLinkedToProducer = true)
    {
        var salt = PasswordSaltValueObject.Build();

        return new CustomerCredentialsModel(
            Id: CUSTOMER_ID,
            PasswordHash: PasswordHashValueObject.Build(PASSWORD, salt, PRIVATE_KEY).Value,
            PasswordSalt: salt.Value,
            IsLinkedToProducer: isLinkedToProducer);
    }

    private void GivenCustomerFoundByEmail(bool isLinkedToProducer = true)
        => _repository
            .FindCredentialsByEmailAsync(EMAIL, PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns(BuildCredentials(isLinkedToProducer));

    private void ThenTokenWasNotGenerated()
        => _tokenGenerator
            .DidNotReceive()
            .Generate(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>());

    private void ThenOutcomeRecorded(string outcome)
    {
        var request = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.customers.authentication.requests");
        Assert.Equal(1, request.Value);

        var recorded = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.customers.authentication.outcomes");
        Assert.Equal(outcome, recorded.Outcome);
    }

    [Fact]
    public async Task Given_ValidEmailAndPassword_When_Executing_Should_ReturnTokenValidForFourHours()
    {
        // Arrange
        GivenCustomerFoundByEmail();

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ACCESS_TOKEN, result.Output!.AccessToken);
        _tokenGenerator
            .Received(1)
            .Generate(CUSTOMER_ID, PRODUCER_ID, Arg.Any<DateTime>(), result.Output.ExpiresAt);
        var issuedAt = (DateTime)_tokenGenerator.ReceivedCalls().Single().GetArguments()[2]!;
        Assert.Equal(TimeSpan.FromHours(4), result.Output.ExpiresAt - issuedAt);
        ThenOutcomeRecorded("authenticated");
    }

    [Fact]
    public async Task Given_ValidDocumentAndPassword_When_Executing_Should_FindByNormalizedDocumentAndAuthenticate()
    {
        // Arrange
        _repository
            .FindCredentialsByDocumentAsync(DOCUMENT, PRODUCER_ID, Arg.Any<CancellationToken>())
            .Returns(BuildCredentials());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(email: null, document: "529.982.247-25"), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository.DidNotReceive().FindCredentialsByEmailAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EmailAndDocument_When_Executing_Should_ReturnOnlyOneLoginNotificationWithoutQuerying()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(document: DOCUMENT), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("LOGIN_MUST_BE_ONLY_ONE", notification.Code);
        await _repository.DidNotReceive().FindCredentialsByEmailAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("validation_failed");
    }

    [Fact]
    public async Task Given_NoEmailAndNoDocument_When_Executing_Should_ReturnLoginRequiredNotification()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(email: "  ", document: null), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("LOGIN_REQUIRED", notification.Code);
    }

    [Fact]
    public async Task Given_NoProducerId_When_Executing_Should_ReturnProducerRequiredNotification()
    {
        // Arrange
        var input = AuthenticateCustomerUseCaseInput.Create(EMAIL, null, PASSWORD, null);

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("PRODUCER_ID_REQUIRED", notification.Code);
    }

    [Fact]
    public async Task Given_MalformedProducerId_When_Executing_Should_ReturnInvalidIdNotification()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(producerId: "invalid"), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("INVALID_ID", notification.Code);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_ReturnItsNotificationsWithoutQuerying()
    {
        // Arrange
        var auditableInfo = AuditableInfoValueObject.Build(null, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), auditableInfo);

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("AUDITABLE_CORRELATION_ID_REQUIRED", notification.Code);
        await _repository.DidNotReceive().FindCredentialsByEmailAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnknownLogin_When_Executing_Should_ReturnFailureAndLogObfuscatedEmail()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
        ThenTokenWasNotGenerated();
        ThenOutcomeRecorded("customer_not_found");
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        Assert.DoesNotContain(EMAIL, log.Message);
    }

    [Fact]
    public async Task Given_UnknownDocument_When_Executing_Should_LogObfuscatedDocument()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(email: null, document: DOCUMENT), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Contains(OBFUSCATED_DOCUMENT, log.Message);
        Assert.DoesNotContain(DOCUMENT, log.Message);
    }

    [Fact]
    public async Task Given_WrongPassword_When_Executing_Should_ReturnSameFailureWithoutToken()
    {
        // Arrange
        GivenCustomerFoundByEmail();

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(password: WRONG_PASSWORD), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
        ThenTokenWasNotGenerated();
        ThenOutcomeRecorded("invalid_password");
        Assert.All(_logger.Logs, log => Assert.DoesNotContain(WRONG_PASSWORD, log.Message));
    }

    [Fact]
    public async Task Given_CustomerNotLinkedToProducer_When_Executing_Should_ReturnSameFailureWithoutToken()
    {
        // Arrange
        GivenCustomerFoundByEmail(isLinkedToProducer: false);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
        ThenTokenWasNotGenerated();
        ThenOutcomeRecorded("not_linked_to_producer");
    }

    [Fact]
    public async Task Given_RepositoryFailure_When_Executing_Should_LogErrorAndPropagateException()
    {
        // Arrange
        var failure = new InvalidOperationException("database unavailable");
        _repository
            .FindCredentialsByEmailAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        Assert.Same(failure, thrown);
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Error, log.Level);
        ThenOutcomeRecorded("error");
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _repository
            .FindCredentialsByEmailAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AuthenticateCustomerUseCase.AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_CODE, notification.Code);
        Assert.Empty(_logger.Logs);
    }

    [Fact]
    public async Task Given_ValidCredentials_When_Executing_Should_WriteLogInTeamFormat()
    {
        // Arrange
        GivenCustomerFoundByEmail();

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.StartsWith("[AuthenticateCustomerUseCase] Customer has been authenticated. Input = ", log.Message);
        Assert.Contains(CUSTOMER_ID.ToString(), log.Message);
        Assert.Contains(PRODUCER_ID.ToString(), log.Message);
    }

    [Fact]
    public async Task Given_ValidCredentials_When_Executing_Should_NotLogPersonalData()
    {
        // Arrange
        GivenCustomerFoundByEmail();

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.DoesNotContain(EMAIL, log.Message);
        Assert.DoesNotContain(PASSWORD, log.Message);
        Assert.DoesNotContain(ACCESS_TOKEN, log.Message);
    }
}
