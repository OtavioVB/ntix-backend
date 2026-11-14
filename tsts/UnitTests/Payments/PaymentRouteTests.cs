using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Payments;

public sealed class PaymentRouteTests
{
    private readonly ProducerIntegration _abacatePay = PaymentTestData.Integration();
    private readonly ProducerIntegration _abacatePayBackup = PaymentTestData.Integration(name: "AbacatePay - conta reserva");
    private readonly ProducerIntegration _pixOnly = PaymentTestData.Integration(PaymentTestData.PixOnlyProvider(), name: "Só Pix");

    private Domain.CrossCutting.MethodResultContext.MethodResult<PaymentRoute> Build(PaymentMethod paymentMethod, params string?[] integrationIds)
        => PaymentRoute.Build(PaymentTestData.PRODUCER_ID, paymentMethod, integrationIds, [_abacatePay, _abacatePayBackup, _pixOnly]);

    [Fact]
    public void Given_IntegrationsInOrder_When_Building_Should_KeepOrderAsPriority()
    {
        // Arrange & Act
        var result = Build(PaymentMethod.PIX, _abacatePayBackup.Id.Value.ToString(), _abacatePay.Id.Value.ToString());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([_abacatePayBackup.Id.Value, _abacatePay.Id.Value], result.Output!.Integrations.Select(integration => integration.Id.Value));
        Assert.Equal(1, PaymentRoute.GetPriority(0));
        Assert.Equal(2, PaymentRoute.GetPriority(1));
    }

    [Fact]
    public void Given_EmptyList_When_Building_Should_ReturnEmptyRoute()
    {
        // Arrange & Act
        var result = Build(PaymentMethod.BOLETO);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Output!.Integrations);
    }

    [Fact]
    public void Given_SameIntegrationTwice_When_Building_Should_ReturnIntegrationDuplicatedInRoute()
    {
        // Arrange & Act
        var result = Build(PaymentMethod.PIX, _abacatePay.Id.Value.ToString(), _abacatePay.Id.Value.ToString());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_DUPLICATED_IN_ROUTE");
    }

    [Fact]
    public void Given_ProviderWithoutTheMethod_When_Building_Should_ReturnProviderDoesNotSupportMethod()
    {
        // Arrange & Act
        var result = Build(PaymentMethod.BOLETO, _abacatePay.Id.Value.ToString(), _pixOnly.Id.Value.ToString());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "PROVIDER_DOES_NOT_SUPPORT_METHOD");
    }

    [Fact]
    public void Given_IntegrationOfAnotherProducer_When_Building_Should_ReturnIntegrationNotFound()
    {
        // Arrange
        var otherProducerIntegration = PaymentTestData.Integration(producerId: IdValueObject.Build());

        // Act
        var result = Build(PaymentMethod.PIX, otherProducerIntegration.Id.Value.ToString());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_NOT_FOUND");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-uuid")]
    public void Given_MalformedId_When_Building_Should_ReturnIntegrationNotFound(string? integrationId)
    {
        // Arrange & Act
        var result = Build(PaymentMethod.PIX, integrationId);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "INTEGRATION_NOT_FOUND");
    }

    [Fact]
    public void Given_InactiveIntegration_When_Building_Should_KeepItInRoute()
    {
        // Arrange
        var inactive = PaymentTestData.Integration(status: IntegrationStatus.INACTIVE, name: "Inativa");

        // Act
        var result = PaymentRoute.Build(PaymentTestData.PRODUCER_ID, PaymentMethod.PIX, [inactive.Id.Value.ToString()], [inactive]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Output!.Integrations);
    }
}
