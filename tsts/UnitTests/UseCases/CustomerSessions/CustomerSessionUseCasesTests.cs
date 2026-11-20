using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomerSession;
using Application.UseCases.CreateCustomerSession.Inputs;
using Application.UseCases.GetCurrentCustomer;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Application.UseCases.RefreshCustomerSession;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Application.UseCases.RevokeCustomerSession;
using Application.UseCases.RevokeCustomerSession.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.CustomerSessions;

public sealed class CustomerSessionUseCasesTests : IDisposable
{
    private static readonly DateTime NOW = ProducerSessionTestData.NOW.UtcDateTime;

    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerSessionRepository _sessionRepository = Substitute.For<ICustomerSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly FakeTimeProvider _timeProvider = new(ProducerSessionTestData.NOW);
    private readonly CapturingLogger<CreateCustomerSessionUseCase> _createLogger = new();
    private readonly CustomerAccessTokenIssuer _issuer = new(ProducerSessionTestData.JWT_CONFIGURATION);
    private readonly CustomerModel _customer = CustomerSessionTestData.NewCustomer();
    private readonly CreateCustomerSessionUseCase _createUseCase;
    private readonly RefreshCustomerSessionUseCase _refreshUseCase;
    private readonly RevokeCustomerSessionUseCase _revokeUseCase;
    private readonly GetCurrentCustomerUseCase _meUseCase;

    private CustomerSessionModel? _created;

    public CustomerSessionUseCasesTests()
    {
        _customerRepository.GetByEmailAsync(CustomerSessionTestData.EMAIL, Arg.Any<CancellationToken>()).Returns(_customer);
        _customerRepository.GetByDocumentAsync(CustomerSessionTestData.CPF, Arg.Any<CancellationToken>()).Returns(_customer);
        _customerRepository.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _sessionRepository.CreateAsync(Arg.Do<CustomerSessionModel>(model => _created = model), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _measurements = new MeasurementCollector(_meterFactory);

        var sessionConfiguration = new CustomerSessionConfiguration();
        var limiter = new LoginAttemptLimiter(new LoginAttemptConfiguration(), _timeProvider);

        _createUseCase = new CreateCustomerSessionUseCase(
            _customerRepository, _sessionRepository, _unitOfWork, limiter, _issuer, sessionConfiguration,
            new PasswordConfiguration(ProducerSessionTestData.PRIVATE_KEY), _timeProvider,
            new CreateCustomerSessionDiagnostics(_meterFactory), new CreateCustomerDiagnostics(_meterFactory), _createLogger);
        _refreshUseCase = new RefreshCustomerSessionUseCase(
            _customerRepository, _sessionRepository, _unitOfWork, _issuer, sessionConfiguration, _timeProvider,
            new RefreshCustomerSessionDiagnostics(_meterFactory), new CapturingLogger<RefreshCustomerSessionUseCase>());
        _revokeUseCase = new RevokeCustomerSessionUseCase(_sessionRepository, _unitOfWork, _timeProvider, new RevokeCustomerSessionDiagnostics(_meterFactory), new CapturingLogger<RevokeCustomerSessionUseCase>());
        _meUseCase = new GetCurrentCustomerUseCase(_customerRepository, _unitOfWork, new GetCurrentCustomerDiagnostics(_meterFactory), new CapturingLogger<GetCurrentCustomerUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void ThenOutcomeRecorded(string instrument, string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == instrument && measurement.Value == 1).Outcome);

    private static AuditableInfoValueObject Auditable()
        => ProducerSessionTestData.BuildAuditableInfo();

    [Theory]
    [InlineData(CustomerSessionTestData.EMAIL)]
    [InlineData("MARINA@exemplo.com")]
    [InlineData(CustomerSessionTestData.MASKED_CPF)]
    [InlineData(CustomerSessionTestData.CPF)]
    public async Task Given_EmailOrCpfAndRightPassword_When_LoggingIn_Should_OpenSessionWithCustomerToken(string login)
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(login, CustomerSessionTestData.PASSWORD), Auditable());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Marina", result.Output!.Customer.FirstName);
        Assert.Equal(30 * 24 * 60 * 60, result.Output.RefreshTokenExpiresIn);
        Assert.Equal(_customer.Id, _created!.CustomerId);
        Assert.Equal(RefreshTokenValueObject.Build(result.Output.RefreshToken).Hash, _created.RefreshTokenHash);

        var token = new JsonWebToken(result.Output.AccessToken);
        Assert.Equal(_customer.Id.ToString(), token.Subject);
        Assert.Equal("CUSTOMER", token.GetClaim("actor").Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Value.Contains("Marina") || claim.Value.Contains(CustomerSessionTestData.CPF) || claim.Value.Contains('@'));
        ThenOutcomeRecorded("ntix.customers.sessions.create.outcomes", CreateCustomerSessionDiagnostics.OUTCOME_CREATED);
    }

    [Theory]
    [InlineData(CustomerSessionTestData.EMAIL, "Senha@errada1")]
    [InlineData("ninguem@exemplo.com", CustomerSessionTestData.PASSWORD)]
    [InlineData("123.456.789-00", CustomerSessionTestData.PASSWORD)]
    [InlineData("isto não é login", CustomerSessionTestData.PASSWORD)]
    public async Task Given_UnknownAccountOrWrongPassword_When_LoggingIn_Should_AnswerTheSameWithoutRevealingTheLogin(string login, string password)
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(login, password), Auditable());

        // Assert
        Assert.Equal(CreateCustomerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceive().CreateAsync(Arg.Any<CustomerSessionModel>(), Arg.Any<CancellationToken>());
        Assert.All(_createLogger.Logs, log => Assert.DoesNotContain(CustomerSessionTestData.EMAIL, log.Message));
    }

    [Fact]
    public async Task Given_EmptyLogin_When_LoggingIn_Should_ReportLoginRequired()
    {
        // Arrange & Act
        var result = await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(" ", CustomerSessionTestData.PASSWORD), Auditable());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "LOGIN_REQUIRED");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_TenAttemptsWithMaskedAndPlainCpf_When_LoggingInAgain_Should_BeRateLimited()
    {
        // Arrange
        for (var attempt = 0; attempt < 10; attempt++)
            await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(attempt % 2 == 0 ? CustomerSessionTestData.CPF : CustomerSessionTestData.MASKED_CPF, "Senha@errada1"), Auditable());

        // Act
        var result = await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(CustomerSessionTestData.MASKED_CPF, CustomerSessionTestData.PASSWORD), Auditable());

        // Assert
        Assert.Equal(CreateCustomerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_ActiveRefreshToken_When_Refreshing_Should_RotateInsideTheFamily()
    {
        // Arrange
        var token = RefreshTokenValueObject.Generate();
        var session = CustomerSessionTestData.NewSession(_customer.Id, token.Hash);
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Is<byte[]>(hash => hash.SequenceEqual(token.Hash)), Arg.Any<CancellationToken>()).Returns(session);
        CustomerSessionModel? replaced = null;
        CustomerSessionModel? next = null;
        _sessionRepository.ReplaceAsync(Arg.Do<CustomerSessionModel>(model => replaced = model), Arg.Do<CustomerSessionModel>(model => next = model), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(token.Value), Auditable());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEqual(token.Value, result.Output!.RefreshToken);
        Assert.Equal(next!.Id, replaced!.ReplacedById);
        Assert.Equal(NOW, replaced.RevokedAt);
        Assert.Equal(session.FamilyId, next.FamilyId);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ReplacedRefreshToken_When_Refreshing_Should_RevokeTheFamily()
    {
        // Arrange
        var token = RefreshTokenValueObject.Generate();
        var session = CustomerSessionTestData.NewSession(_customer.Id, token.Hash, revokedAt: NOW.AddHours(-1), replacedById: Guid.NewGuid());
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(session);

        // Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(token.Value), Auditable());

        // Assert
        Assert.Equal(RefreshCustomerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.Received(1).RevokeFamilyAsync(session.FamilyId, NOW, Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("ntix.customers.sessions.refresh.outcomes", RefreshCustomerSessionDiagnostics.OUTCOME_REUSE_DETECTED);
    }

    [Theory]
    [InlineData("expirado")]
    [InlineData("revogado")]
    [InlineData("inexistente")]
    [InlineData("fora-do-formato")]
    public async Task Given_UnusableRefreshToken_When_Refreshing_Should_ReportInvalidToken(string situation)
    {
        // Arrange
        var token = RefreshTokenValueObject.Generate();
        CustomerSessionModel? session = situation switch
        {
            "expirado" => CustomerSessionTestData.NewSession(_customer.Id, token.Hash, expiresAt: NOW.AddSeconds(-1)),
            "revogado" => CustomerSessionTestData.NewSession(_customer.Id, token.Hash, revokedAt: NOW.AddMinutes(-1)),
            _ => null,
        };
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(session);

        // Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(situation == "fora-do-formato" ? "abc" : token.Value), Auditable());

        // Assert
        Assert.Equal(RefreshCustomerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _sessionRepository.DidNotReceive().ReplaceAsync(Arg.Any<CustomerSessionModel>(), Arg.Any<CustomerSessionModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_KnownRefreshToken_When_Revoking_Should_RevokeTheFamily()
    {
        // Arrange
        var token = RefreshTokenValueObject.Generate();
        var session = CustomerSessionTestData.NewSession(_customer.Id, token.Hash);
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(session);

        // Act
        var result = await _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(token.Value), Auditable());

        // Assert
        Assert.True(result.IsSuccess);
        await _sessionRepository.Received(1).RevokeFamilyAsync(session.FamilyId, NOW, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnknownRefreshToken_When_Revoking_Should_SucceedWithoutRevealingIt()
    {
        // Arrange & Act
        var result = await _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create("abc"), Auditable());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_CustomerOfTheToken_When_GettingOwnData_Should_ObfuscateTheCpf()
    {
        // Arrange & Act
        var result = await _meUseCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(_customer.Id.ToString()), Auditable());

        // Assert
        Assert.Equal("***.982.247-**", result.Output!.Document);
        Assert.Equal("Marina Souza", result.Output.FullName);
        Assert.Equal(CustomerSessionTestData.EMAIL, result.Output.Email);
    }

    [Theory]
    [InlineData("nao-e-guid")]
    [InlineData("7d3a5b1c-0000-4000-8000-000000000000")]
    public async Task Given_TokenOfMissingCustomer_When_GettingOwnData_Should_ReportNotFound(string customerId)
    {
        // Arrange & Act
        var result = await _meUseCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(customerId), Auditable());

        // Assert
        Assert.Equal(GetCurrentCustomerUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_LoggingIn_Should_Rethrow()
    {
        // Arrange
        _sessionRepository.CreateAsync(Arg.Any<CustomerSessionModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(CustomerSessionTestData.EMAIL, CustomerSessionTestData.PASSWORD), Auditable()));
    }

    [Fact]
    public async Task Given_Cancellation_When_LoggingIn_Should_ReturnCanceledNotification()
    {
        // Arrange
        _customerRepository.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _createUseCase.ExecuteAsync(CreateCustomerSessionUseCaseInput.Create(CustomerSessionTestData.EMAIL, CustomerSessionTestData.PASSWORD), Auditable());

        // Assert
        Assert.Equal(CreateCustomerSessionUseCase.CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
