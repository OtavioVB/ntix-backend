using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
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
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UnitTests.Support;

namespace UnitTests.UseCases.CustomerSessions;

/// <summary>Caminhos de recusa e de falha da renovação, do logout e de meus dados do cliente (REQ12).</summary>
public sealed class CustomerSessionFailureTests : IDisposable
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerSessionRepository _sessionRepository = Substitute.For<ICustomerSessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly RefreshCustomerSessionUseCase _refreshUseCase;
    private readonly RevokeCustomerSessionUseCase _revokeUseCase;
    private readonly GetCurrentCustomerUseCase _meUseCase;
    private readonly RefreshTokenValueObject _token = RefreshTokenValueObject.Generate();

    private static readonly AuditableInfoValueObject INVALID_AUDITABLE_INFO = AuditableInfoValueObject.Build(null, null, null, null);

    public CustomerSessionFailureTests()
    {
        var timeProvider = new FakeTimeProvider(ProducerSessionTestData.NOW);

        _measurements = new MeasurementCollector(_meterFactory);
        _refreshUseCase = new RefreshCustomerSessionUseCase(
            _customerRepository, _sessionRepository, _unitOfWork, new CustomerAccessTokenIssuer(ProducerSessionTestData.JWT_CONFIGURATION),
            new CustomerSessionConfiguration(), timeProvider, new RefreshCustomerSessionDiagnostics(_meterFactory), new CapturingLogger<RefreshCustomerSessionUseCase>());
        _revokeUseCase = new RevokeCustomerSessionUseCase(_sessionRepository, _unitOfWork, timeProvider, new RevokeCustomerSessionDiagnostics(_meterFactory), new CapturingLogger<RevokeCustomerSessionUseCase>());
        _meUseCase = new GetCurrentCustomerUseCase(_customerRepository, _unitOfWork, new GetCurrentCustomerDiagnostics(_meterFactory), new CapturingLogger<GetCurrentCustomerUseCase>());
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void ThenOutcomeRecorded(string instrument, string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == instrument && measurement.Value == 1).Outcome);

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Refreshing_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(_token.Value), INVALID_AUDITABLE_INFO);

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded("ntix.customers.sessions.refresh.outcomes", RefreshCustomerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_SessionOfMissingCustomer_When_Refreshing_Should_ReportInvalidToken()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(CustomerSessionTestData.NewSession(Guid.NewGuid(), _token.Hash));

        // Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo());

        // Assert
        Assert.Equal(RefreshCustomerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Refreshing_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo()));
        ThenOutcomeRecorded("ntix.customers.sessions.refresh.outcomes", RefreshCustomerSessionDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_Refreshing_Should_ReturnCanceledNotification()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _refreshUseCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo());

        // Assert
        Assert.Equal(RefreshCustomerSessionUseCase.REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Revoking_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(_token.Value), INVALID_AUDITABLE_INFO);

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded("ntix.customers.sessions.revoke.outcomes", RevokeCustomerSessionDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_ValidButUnknownToken_When_Revoking_Should_SucceedAsNotFound()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns((CustomerSessionModel?)null);

        // Act
        var result = await _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded("ntix.customers.sessions.revoke.outcomes", RevokeCustomerSessionDiagnostics.OUTCOME_NOT_FOUND);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_Revoking_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo()));
        ThenOutcomeRecorded("ntix.customers.sessions.revoke.outcomes", RevokeCustomerSessionDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_Revoking_Should_ReturnCanceledNotification()
    {
        // Arrange
        _sessionRepository.GetByRefreshTokenHashAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _revokeUseCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(_token.Value), ProducerSessionTestData.BuildAuditableInfo());

        // Assert
        Assert.Equal(RevokeCustomerSessionUseCase.REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_GettingOwnData_Should_ReportValidation()
    {
        // Arrange & Act
        var result = await _meUseCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(Guid.NewGuid().ToString()), INVALID_AUDITABLE_INFO);

        // Assert
        Assert.True(result.IsError);
        ThenOutcomeRecorded("ntix.customers.me.outcomes", GetCurrentCustomerDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_UnexpectedFailure_When_GettingOwnData_Should_RollBackAndRethrowEvenIfRollbackFails()
    {
        // Arrange
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("rollback"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _meUseCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(Guid.NewGuid().ToString()), ProducerSessionTestData.BuildAuditableInfo()));
        ThenOutcomeRecorded("ntix.customers.me.outcomes", GetCurrentCustomerDiagnostics.OUTCOME_ERROR);
    }

    [Fact]
    public async Task Given_Cancellation_When_GettingOwnData_Should_ReturnCanceledNotification()
    {
        // Arrange
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _meUseCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(Guid.NewGuid().ToString()), ProducerSessionTestData.BuildAuditableInfo());

        // Assert
        Assert.Equal(GetCurrentCustomerUseCase.GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
    }
}
