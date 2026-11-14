using Application.UseCases.UpdateProducerIntegration;
using Application.UseCases.UpdateProducerIntegration.Inputs;
using Domain.ValueObjects;
using Infra.Cryptography.Base.Configuration;
using Infra.Cryptography.Credentials;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Enums;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using System.Security.Cryptography;
using UnitTests.Support;

namespace UnitTests.UseCases.Payments;

public sealed class UpdateProducerIntegrationUseCaseTests : IDisposable
{
    private const string SLUG = "produtora-x";

    private readonly Guid _integrationId = Guid.NewGuid();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IPaymentProviderRepository _paymentProviderRepository = Substitute.For<IPaymentProviderRepository>();
    private readonly IProducerIntegrationRepository _producerIntegrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CredentialCipher _cipher = new(new PaymentCredentialConfiguration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    private readonly CapturingLogger<UpdateProducerIntegrationUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly UpdateProducerIntegrationUseCase _useCase;

    private ProducerIntegrationModel? _updatedModel;
    private IReadOnlyList<ProducerIntegrationCredentialModel>? _changedCredentials;
    private IReadOnlyList<string>? _removedKeys;

    public UpdateProducerIntegrationUseCaseTests()
    {
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).Returns(PaymentTestData.PRODUCER_ID.Value);
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel()]);
        _producerIntegrationRepository
            .GetByIdForUpdateAsync(PaymentTestData.PRODUCER_ID.Value, _integrationId, Arg.Any<CancellationToken>())
            .Returns(PaymentTestData.IntegrationModel(
                _integrationId,
                credentials: [PaymentTestData.Credential(PaymentTestData.API_KEY, "a91f"), PaymentTestData.Credential(PaymentTestData.WEBHOOK_SECRET, "hook")]));
        _producerIntegrationRepository
            .UpdateAsync(
                Arg.Do<ProducerIntegrationModel>(model => _updatedModel = model),
                Arg.Do<IReadOnlyList<ProducerIntegrationCredentialModel>>(credentials => _changedCredentials = credentials),
                Arg.Do<IReadOnlyList<string>>(keys => _removedKeys = keys),
                Arg.Any<CancellationToken>())
            .Returns(ProducerIntegrationUpdateResult.Updated);

        _useCase = new UpdateProducerIntegrationUseCase(
            producerRepository: _producerRepository,
            paymentProviderRepository: _paymentProviderRepository,
            producerIntegrationRepository: _producerIntegrationRepository,
            credentialCipher: _cipher,
            unitOfWork: _unitOfWork,
            diagnostics: new UpdateProducerIntegrationDiagnostics(_meterFactory),
            logger: _logger);
    }

    public void Dispose()
        => _meterFactory.Dispose();

    private UpdateProducerIntegrationUseCaseInput BuildInput(string? name = null, string? status = null, Dictionary<string, string?>? credentials = null, string? integrationId = null)
        => UpdateProducerIntegrationUseCaseInput.Create(SLUG, integrationId ?? _integrationId.ToString(), name, status, credentials);

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(Guid.NewGuid().ToString(), "ntix-web", "1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "2026-11-14T12:00:00.000Z");

    [Fact]
    public async Task Given_InactiveStatus_When_Executing_Should_InactivateKeepingCredentialsAndCommit()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(status: "INACTIVE"), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", result.Output!.Status);
        Assert.Equal("INACTIVE", _updatedModel!.Status);
        Assert.Empty(_changedCredentials!);
        Assert.Empty(_removedKeys!);
        Assert.Equal(2, result.Output.Credentials.Count);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Logs, log => log.EventId == 4402);
    }

    [Fact]
    public async Task Given_NewApiKey_When_Executing_Should_ReplaceOnlyThatKey()
    {
        // Arrange
        const string NEW_KEY = "abc_prod_nova-chave-7c2e";

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(credentials: new Dictionary<string, string?> { [PaymentTestData.API_KEY] = NEW_KEY }), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        var changed = Assert.Single(_changedCredentials!);
        Assert.Equal(PaymentTestData.API_KEY, changed.Key);
        Assert.Equal("7c2e", changed.ValueHint);
        Assert.Contains(result.Output!.Credentials, credential => credential.Key == PaymentTestData.API_KEY && credential.ValueHint == "7c2e");
        Assert.Contains(result.Output.Credentials, credential => credential.Key == PaymentTestData.WEBHOOK_SECRET && credential.ValueHint == "hook");
        Assert.All(_logger.Logs, log => Assert.DoesNotContain(NEW_KEY, log.Message));
    }

    [Fact]
    public async Task Given_NullOptionalCredential_When_Executing_Should_RemoveIt()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(credentials: new Dictionary<string, string?> { [PaymentTestData.WEBHOOK_SECRET] = null }), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([PaymentTestData.WEBHOOK_SECRET], _removedKeys!);
        Assert.DoesNotContain(result.Output!.Credentials, credential => credential.Key == PaymentTestData.WEBHOOK_SECRET);
    }

    [Fact]
    public async Task Given_NullRequiredCredential_When_Executing_Should_ReturnCredentialKeyRequiredAndRollBack()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(credentials: new Dictionary<string, string?> { [PaymentTestData.API_KEY] = null }), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_REQUIRED");
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidStatus_When_Executing_Should_ReturnIntegrationStatusInvalid()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(status: "DELETED"), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_STATUS_INVALID");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("nao-e-um-uuid")]
    [InlineData("6b0f0000-0000-4000-8000-000000000000")]
    public async Task Given_IntegrationOutsideTheProducer_When_Executing_Should_ReturnIntegrationNotFound(string integrationId)
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(status: "INACTIVE", integrationId: integrationId), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == UpdateProducerIntegrationUseCase.INTEGRATION_NOT_FOUND_NOTIFICATION_CODE);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NameUsedByAnotherIntegration_When_Executing_Should_ReturnIntegrationNameAlreadyExists()
    {
        // Arrange
        _producerIntegrationRepository
            .UpdateAsync(Arg.Any<ProducerIntegrationModel>(), Arg.Any<IReadOnlyList<ProducerIntegrationCredentialModel>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ProducerIntegrationUpdateResult.NameAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(name: "Outra"), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == UpdateProducerIntegrationUseCase.INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}
