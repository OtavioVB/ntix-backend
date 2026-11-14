using Application.UseCases.GetPaymentRoutes;
using Application.UseCases.GetPaymentRoutes.Inputs;
using Application.UseCases.RankPaymentIntegrations;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.ReplacePaymentRoute;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using NSubstitute;
using UnitTests.Support;

namespace UnitTests.UseCases.Payments;

public sealed class PaymentRoutesUseCasesTests : IDisposable
{
    private const string SLUG = "produtora-x";

    private readonly Guid _principalId = Guid.NewGuid();
    private readonly Guid _backupId = Guid.NewGuid();
    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IPaymentProviderRepository _paymentProviderRepository = Substitute.For<IPaymentProviderRepository>();
    private readonly IProducerIntegrationRepository _producerIntegrationRepository = Substitute.For<IProducerIntegrationRepository>();
    private readonly IPaymentRouteRepository _paymentRouteRepository = Substitute.For<IPaymentRouteRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;

    private IReadOnlyList<PaymentRouteModel>? _replacedRoutes;

    public PaymentRoutesUseCasesTests()
    {
        _producerRepository.GetIdBySlugAsync(SLUG, Arg.Any<CancellationToken>()).Returns(PaymentTestData.PRODUCER_ID.Value);
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel()]);
        GivenIntegrations(principalStatus: "ACTIVE");
        _paymentRouteRepository
            .ReplaceAsync(PaymentTestData.PRODUCER_ID.Value, Arg.Any<string>(), Arg.Do<IReadOnlyList<PaymentRouteModel>>(routes => _replacedRoutes = routes), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _measurements = new MeasurementCollector(_meterFactory);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void GivenIntegrations(string principalStatus)
        => _producerIntegrationRepository
            .GetByProducerAsync(PaymentTestData.PRODUCER_ID.Value, Arg.Any<CancellationToken>())
            .Returns([
                PaymentTestData.IntegrationModel(_principalId, principalStatus, "AbacatePay - conta principal"),
                PaymentTestData.IntegrationModel(_backupId, "ACTIVE", "AbacatePay - conta reserva"),
            ]);

    private void GivenPixRoute(params Guid[] integrationIds)
    {
        var routes = integrationIds
            .Select((id, index) => new PaymentRouteModel(Guid.NewGuid(), PaymentTestData.PRODUCER_ID.Value, "PIX", (short)(index + 1), id, PaymentTestData.NOW.Value, PaymentTestData.CORRELATION_ID.Value))
            .ToArray();

        _paymentRouteRepository.GetByProducerAsync(PaymentTestData.PRODUCER_ID.Value, Arg.Any<CancellationToken>()).Returns(routes);
        _paymentRouteRepository.GetByProducerAndMethodAsync(PaymentTestData.PRODUCER_ID.Value, "PIX", Arg.Any<CancellationToken>()).Returns(routes);
    }

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(Guid.NewGuid().ToString(), "ntix-web", "1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "2026-11-14T12:00:00.000Z");

    private ReplacePaymentRouteUseCase BuildReplaceUseCase()
        => new(_producerRepository, _paymentProviderRepository, _producerIntegrationRepository, _paymentRouteRepository, _unitOfWork, new ReplacePaymentRouteDiagnostics(_meterFactory), new CapturingLogger<ReplacePaymentRouteUseCase>());

    private RankPaymentIntegrationsUseCase BuildRankUseCase()
        => new(_paymentProviderRepository, _producerIntegrationRepository, _paymentRouteRepository, _unitOfWork, new RankPaymentIntegrationsDiagnostics(_meterFactory), new CapturingLogger<RankPaymentIntegrationsUseCase>());

    [Fact]
    public async Task Given_TwoIntegrations_When_ReplacingPixRoute_Should_SavePrioritiesInOrderAndCommit()
    {
        // Arrange
        var input = ReplacePaymentRouteUseCaseInput.Create(SLUG, "PIX", [_backupId.ToString(), _principalId.ToString()]);

        // Act
        var result = await BuildReplaceUseCase().ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([(1, _backupId), (2, _principalId)], _replacedRoutes!.Select(route => ((int)route.Priority, route.IntegrationId)));
        Assert.All(_replacedRoutes!, route => Assert.Equal("PIX", route.PaymentMethod));
        Assert.Equal([1, 2], result.Output!.Integrations.Select(item => item.Priority));
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EmptyList_When_ReplacingRoute_Should_RemoveTheMethod()
    {
        // Arrange
        var input = ReplacePaymentRouteUseCaseInput.Create(SLUG, "BOLETO", []);

        // Act
        var result = await BuildReplaceUseCase().ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(_replacedRoutes!);
        Assert.Empty(result.Output!.Integrations);
    }

    [Fact]
    public async Task Given_DuplicatedIntegration_When_ReplacingRoute_Should_RollBackWithoutReplacing()
    {
        // Arrange
        var input = ReplacePaymentRouteUseCaseInput.Create(SLUG, "PIX", [_principalId.ToString(), _principalId.ToString()]);

        // Act
        var result = await BuildReplaceUseCase().ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_DUPLICATED_IN_ROUTE");
        await _paymentRouteRepository.DidNotReceive().ReplaceAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<PaymentRouteModel>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidMethod_When_ReplacingRoute_Should_ReturnPaymentMethodInvalid()
    {
        // Arrange
        var input = ReplacePaymentRouteUseCaseInput.Create(SLUG, "CARD", [_principalId.ToString()]);

        // Act
        var result = await BuildReplaceUseCase().ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "PAYMENT_METHOD_INVALID");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_MissingList_When_ReplacingRoute_Should_ReturnIntegrationIdsRequired()
    {
        // Arrange
        var input = ReplacePaymentRouteUseCaseInput.Create(SLUG, "PIX", null);

        // Act
        var result = await BuildReplaceUseCase().ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_IDS_REQUIRED");
    }

    [Fact]
    public async Task Given_RoutesOfOneMethod_When_GettingRoutes_Should_ReturnTheThreeMethods()
    {
        // Arrange
        GivenPixRoute(_principalId, _backupId);
        var useCase = new GetPaymentRoutesUseCase(_producerRepository, _paymentProviderRepository, _producerIntegrationRepository, _paymentRouteRepository, _unitOfWork, new GetPaymentRoutesDiagnostics(_meterFactory), new CapturingLogger<GetPaymentRoutesUseCase>());

        // Act
        var result = await useCase.ExecuteAsync(GetPaymentRoutesUseCaseInput.Create(SLUG), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(["BOLETO", "CREDIT_CARD", "PIX"], result.Output!.Routes.Keys.Order());
        Assert.Equal([_principalId, _backupId], result.Output.Routes["PIX"].Select(item => item.IntegrationId));
        Assert.Empty(result.Output.Routes["BOLETO"]);
        Assert.Empty(result.Output.Routes["CREDIT_CARD"]);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_PixRoute_When_Ranking_Should_ReturnPrincipalThenFallback()
    {
        // Arrange
        GivenPixRoute(_principalId, _backupId);

        // Act
        var result = await BuildRankUseCase().ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(PaymentTestData.PRODUCER_ID.Value, "PIX"), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([(1, _principalId), (2, _backupId)], result.Output!.Integrations.Select(item => (item.Priority, item.IntegrationId)));
        Assert.All(result.Output.Integrations, item => Assert.Equal(PaymentTestData.ABACATEPAY, item.ProviderCode));
    }

    [Fact]
    public async Task Given_InactivePrincipal_When_Ranking_Should_ReturnOnlyTheFallbackWithItsPriority()
    {
        // Arrange
        GivenIntegrations(principalStatus: "INACTIVE");
        GivenPixRoute(_principalId, _backupId);

        // Act
        var result = await BuildRankUseCase().ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(PaymentTestData.PRODUCER_ID.Value, "PIX"), BuildAuditableInfo());

        // Assert
        var ranked = Assert.Single(result.Output!.Integrations);
        Assert.Equal(_backupId, ranked.IntegrationId);
        Assert.Equal(2, ranked.Priority);
    }

    [Fact]
    public async Task Given_ProviderDisabledByNtix_When_Ranking_Should_ReturnUnavailableAndCountEmptyRanking()
    {
        // Arrange
        _paymentProviderRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([PaymentTestData.AbacatePayModel("DISABLED")]);
        GivenPixRoute(_principalId);

        // Act
        var result = await BuildRankUseCase().ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(PaymentTestData.PRODUCER_ID.Value, "PIX"), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "PAYMENT_METHOD_UNAVAILABLE");
        var empty = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.payments.ranking.empty");
        Assert.Equal(1, empty.Value);
    }

    [Fact]
    public async Task Given_MethodWithoutRoute_When_Ranking_Should_ReturnUnavailableWithoutReadingIntegrations()
    {
        // Arrange
        _paymentRouteRepository.GetByProducerAndMethodAsync(PaymentTestData.PRODUCER_ID.Value, "BOLETO", Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var result = await BuildRankUseCase().ExecuteAsync(RankPaymentIntegrationsUseCaseInput.Create(PaymentTestData.PRODUCER_ID.Value, "BOLETO"), BuildAuditableInfo());

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "PAYMENT_METHOD_UNAVAILABLE");
        await _producerIntegrationRepository.DidNotReceive().GetByProducerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
