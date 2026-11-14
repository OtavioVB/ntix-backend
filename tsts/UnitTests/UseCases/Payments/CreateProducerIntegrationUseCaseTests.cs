using Application.UseCases.CreateProducerIntegration;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Domain.ValueObjects;
using Infra.Cryptography.Base.Configuration;
using Infra.Cryptography.Credentials;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Enums;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using System.Text;
using UnitTests.Support;

namespace UnitTests.UseCases.Payments;

public sealed class CreateProducerIntegrationUseCaseTests : IDisposable
{
    private const string SLUG = "produtora-x";
    private const string NAME = "AbacatePay - conta principal";

    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IPaymentProviderRepository _paymentProviderRepository = Substitute.For<IPaymentProviderRepository>();
    private readonly IProducerIntegrationRepository _producerIntegrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CredentialCipher _cipher = new(new PaymentCredentialConfiguration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    private readonly CapturingLogger<CreateProducerIntegrationUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CreateProducerIntegrationUseCase _useCase;

    private ProducerIntegrationModel? _savedModel;

    public CreateProducerIntegrationUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).Returns(PaymentTestData.PRODUCER_ID.Value);
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel()]);
        _producerIntegrationRepository
            .CreateAsync(Arg.Do<ProducerIntegrationModel>(model => _savedModel = model), Arg.Any<CancellationToken>())
            .Returns(ProducerIntegrationCreationResult.Created);

        var diagnostics = new CreateProducerIntegrationDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new CreateProducerIntegrationUseCase(
            producerRepository: _producerRepository,
            paymentProviderRepository: _paymentProviderRepository,
            producerIntegrationRepository: _producerIntegrationRepository,
            credentialCipher: _cipher,
            unitOfWork: _unitOfWork,
            diagnostics: diagnostics,
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static CreateProducerIntegrationUseCaseInput BuildInput(string providerCode = PaymentTestData.ABACATEPAY, string name = NAME, Dictionary<string, string?>? credentials = null)
        => CreateProducerIntegrationUseCaseInput.Create(
            producerSlug: SLUG,
            providerCode: providerCode,
            name: name,
            credentials: credentials ?? new Dictionary<string, string?> { [PaymentTestData.API_KEY] = PaymentTestData.API_KEY_VALUE });

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(PaymentTestData.CORRELATION_ID.Value.ToString(), "ntix-web", "1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "2026-11-14T12:00:00.000Z");

    private async Task ThenRolledBackWithoutSaving()
    {
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _producerIntegrationRepository.DidNotReceive().CreateAsync(Arg.Any<ProducerIntegrationModel>(), Arg.Any<CancellationToken>());
    }

    private void ThenLogsDoNotExposeCredentials()
        => Assert.All(_logger.Logs, log => Assert.DoesNotContain(PaymentTestData.API_KEY_VALUE, log.Message));

    private void ThenOutcomeRecorded(string outcome)
        => Assert.Equal(outcome, Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.integrations.creation.outcomes").Outcome);

    [Fact]
    public async Task Given_ValidCredentials_When_Executing_Should_SaveEncryptedCredentialAndCommit()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        Assert.NotNull(_savedModel);
        Assert.Equal("ACTIVE", _savedModel.Status);
        Assert.Equal(PaymentTestData.PRODUCER_ID.Value, _savedModel.ProducerId);
        Assert.Equal(PaymentTestData.ABACATEPAY_ID, _savedModel.ProviderId);
        Assert.Equal(PaymentTestData.CORRELATION_ID.Value, _savedModel.CreatedCorrelationId);

        var credential = Assert.Single(_savedModel.Credentials);
        Assert.Equal(PaymentTestData.API_KEY, credential.Key);
        Assert.Equal(PaymentTestData.API_KEY_HINT, credential.ValueHint);
        Assert.DoesNotContain(PaymentTestData.API_KEY_VALUE, Encoding.UTF8.GetString(credential.EncryptedValue));

        ThenOutcomeRecorded(CreateProducerIntegrationDiagnostics.OUTCOME_CREATED);
        ThenLogsDoNotExposeCredentials();
    }

    [Fact]
    public async Task Given_ValidCredentials_When_Executing_Should_ReturnOnlyTheHintOfTheSecret()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var credential = Assert.Single(result.Output!.Credentials);
        Assert.True(credential.IsSecret);
        Assert.Equal(PaymentTestData.API_KEY_HINT, credential.ValueHint);
        Assert.Null(credential.Value);
        Assert.Equal(PaymentTestData.ABACATEPAY, result.Output.ProviderCode);
        Assert.Equal("ACTIVE", result.Output.Status);
    }

    [Fact]
    public async Task Given_ValidCredentials_When_Executing_Should_LogTheKeyButNeverTheValue()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var created = Assert.Single(_logger.Logs, log => log.EventId == 4201);
        Assert.Contains(PaymentTestData.API_KEY, created.Message);
        Assert.Contains(SLUG, created.Message);
        ThenLogsDoNotExposeCredentials();
    }

    [Fact]
    public async Task Given_MissingRequiredCredential_When_Executing_Should_ReturnCredentialKeyRequiredAndRollBack()
    {
        // Arrange
        var input = BuildInput(credentials: new Dictionary<string, string?> { [PaymentTestData.WEBHOOK_SECRET] = "segredo-do-webhook" });

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_REQUIRED");
        await ThenRolledBackWithoutSaving();
        ThenOutcomeRecorded(CreateProducerIntegrationDiagnostics.OUTCOME_VALIDATION_FAILED);
    }

    [Fact]
    public async Task Given_UnknownCredentialKey_When_Executing_Should_ReturnCredentialKeyUnknown()
    {
        // Arrange
        var input = BuildInput(credentials: new Dictionary<string, string?> { [PaymentTestData.API_KEY] = PaymentTestData.API_KEY_VALUE, ["SENHA"] = "123456" });

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_UNKNOWN");
        await ThenRolledBackWithoutSaving();
    }

    [Theory]
    [InlineData("ASAAS")]
    [InlineData("abacatepay")]
    [InlineData(null)]
    public async Task Given_ProviderOutOfTheCatalog_When_Executing_Should_ReturnProviderNotFound(string? providerCode)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(providerCode: providerCode!), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "PROVIDER_NOT_FOUND");
        await ThenRolledBackWithoutSaving();
    }

    [Fact]
    public async Task Given_ProviderDisabledByNtix_When_Executing_Should_ReturnProviderNotFound()
    {
        // Arrange
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel("DISABLED")]);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "PROVIDER_NOT_FOUND");
        await ThenRolledBackWithoutSaving();
    }

    [Fact]
    public async Task Given_InvalidName_When_Executing_Should_ReturnIntegrationNameInvalidWithoutOpeningTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(name: ""), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_NAME_INVALID");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NameAlreadyUsedByTheProducer_When_Executing_Should_ReturnIntegrationNameAlreadyExists()
    {
        // Arrange
        _producerIntegrationRepository
            .CreateAsync(Arg.Any<ProducerIntegrationModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerIntegrationCreationResult.NameAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == CreateProducerIntegrationUseCase.INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateProducerIntegrationDiagnostics.OUTCOME_NAME_ALREADY_EXISTS);
    }

    [Fact]
    public async Task Given_UnknownProducer_When_Executing_Should_ReturnProducerNotFound()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == CreateProducerIntegrationUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE);
        await ThenRolledBackWithoutSaving();
    }

    [Fact]
    public async Task Given_DatabaseFailure_When_Executing_Should_RollBackAndRethrow()
    {
        // Arrange
        _producerIntegrationRepository
            .CreateAsync(Arg.Any<ProducerIntegrationModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenOutcomeRecorded(CreateProducerIntegrationDiagnostics.OUTCOME_ERROR);
        ThenLogsDoNotExposeCredentials();
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_RollBackAndReturnCanceled()
    {
        // Arrange
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == CreateProducerIntegrationUseCase.CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }
}
