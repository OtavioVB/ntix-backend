using Application.UseCases.GetPaymentProviders;
using Application.UseCases.GetPaymentProviders.Inputs;
using Application.UseCases.GetProducerIntegrations;
using Application.UseCases.GetProducerIntegrations.Inputs;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using System.Text;
using UnitTests.Support;

namespace UnitTests.UseCases.Payments;

public sealed class GetPaymentQueriesUseCasesTests : IDisposable
{
    private const string SLUG = "produtora-x";

    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IPaymentProviderRepository _paymentProviderRepository = Substitute.For<IPaymentProviderRepository>();
    private readonly IProducerIntegrationRepository _producerIntegrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly ICredentialCipher _cipher = Substitute.For<ICredentialCipher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();

    public GetPaymentQueriesUseCasesTests()
    {
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).Returns(PaymentTestData.PRODUCER_ID.Value);
    }

    public void Dispose()
        => _meterFactory.Dispose();

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(Guid.NewGuid().ToString(), "ntix-web", null, "2026-11-14T12:00:00.000Z");

    [Fact]
    public async Task Given_EnabledAndDisabledProviders_When_GettingCatalog_Should_ReturnOnlyEnabled()
    {
        // Arrange
        var disabled = PaymentTestData.AbacatePayModel("DISABLED") with { Id = Guid.NewGuid(), Code = "DESLIGADO" };
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel(), disabled]);
        var useCase = new GetPaymentProvidersUseCase(_paymentProviderRepository, _unitOfWork, new GetPaymentProvidersDiagnostics(_meterFactory), new CapturingLogger<GetPaymentProvidersUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetPaymentProvidersUseCaseInput.Create(), BuildAuditableInfo());

        // Assert
        var provider = Assert.Single(result.Output!.Providers);
        Assert.Equal(PaymentTestData.ABACATEPAY, provider.Code);
        Assert.Equal(["PIX", "BOLETO", "CREDIT_CARD"], provider.PaymentMethods);
        Assert.Contains(provider.CredentialKeys, key => key.Key == PaymentTestData.API_KEY && key.IsSecret && key.IsRequired);
        Assert.Contains(provider.CredentialKeys, key => key.Key == PaymentTestData.WEBHOOK_SECRET && key.IsSecret && !key.IsRequired);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_SecretAndNonSecretCredentials_When_GettingIntegrations_Should_DecryptOnlyTheNonSecret()
    {
        // Arrange
        var pixOnly = new PaymentProviderModel(
            Id: Guid.NewGuid(),
            Code: "SO_PIX",
            Name: "Só Pix",
            Status: "ENABLED",
            PaymentMethods: ["PIX"],
            CredentialKeys: [new ProviderCredentialKeyModel(PaymentTestData.API_KEY, true, true), new ProviderCredentialKeyModel(PaymentTestData.ACCOUNT_ID, false, true)]);
        var integration = PaymentTestData.IntegrationModel(
            credentials: [PaymentTestData.Credential(PaymentTestData.API_KEY, "a91f", [9, 9, 9]), PaymentTestData.Credential(PaymentTestData.ACCOUNT_ID, null, [7, 7, 7])]) with { ProviderId = pixOnly.Id };

        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([pixOnly]);
        _producerIntegrationRepository.GetByProducerAsync(PaymentTestData.PRODUCER_ID.Value, Arg.Any<CancellationToken>()).Returns([integration]);
        _cipher.Decrypt(Arg.Is<byte[]>(value => value.SequenceEqual(new byte[] { 7, 7, 7 })), Arg.Any<byte[]>()).Returns(Encoding.UTF8.GetBytes("123"));

        var useCase = new GetProducerIntegrationsUseCase(_producerRepository, _paymentProviderRepository, _producerIntegrationRepository, _cipher, _unitOfWork, new GetProducerIntegrationsDiagnostics(_meterFactory), new CapturingLogger<GetProducerIntegrationsUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetProducerIntegrationsUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        var output = Assert.Single(result.Output!.Integrations);
        var secret = Assert.Single(output.Credentials, credential => credential.Key == PaymentTestData.API_KEY);
        Assert.Equal("a91f", secret.ValueHint);
        Assert.Null(secret.Value);
        var nonSecret = Assert.Single(output.Credentials, credential => credential.Key == PaymentTestData.ACCOUNT_ID);
        Assert.Equal("123", nonSecret.Value);
        Assert.Null(nonSecret.ValueHint);
        _cipher.DidNotReceive().Decrypt(Arg.Is<byte[]>(value => value.SequenceEqual(new byte[] { 9, 9, 9 })), Arg.Any<byte[]>());
    }

    [Theory]
    [InlineData("Produtora-X")]
    [InlineData("../produtora-x")]
    public async Task Given_NonCanonicalSlug_When_GettingIntegrations_Should_ReturnSlugInvalid(string slug)
    {
        // Arrange
        var useCase = new GetProducerIntegrationsUseCase(_producerRepository, _paymentProviderRepository, _producerIntegrationRepository, _cipher, _unitOfWork, new GetProducerIntegrationsDiagnostics(_meterFactory), new CapturingLogger<GetProducerIntegrationsUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetProducerIntegrationsUseCaseInput.Create(slug), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "SLUG_INVALID");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }
}
