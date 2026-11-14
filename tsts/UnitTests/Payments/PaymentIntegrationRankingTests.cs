using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.BoundedContexts.PaymentContext.Services;
using UnitTests.Support;

namespace UnitTests.Payments;

public sealed class PaymentIntegrationRankingTests
{
    private static PaymentRoute Route(PaymentMethod paymentMethod, params ProducerIntegration[] integrations)
        => new(PaymentTestData.PRODUCER_ID, paymentMethod, integrations);

    [Fact]
    public void Given_TwoAvailableIntegrations_When_Ranking_Should_ReturnBothInPriorityOrder()
    {
        // Arrange
        var principal = PaymentTestData.Integration(name: "Principal");
        var backup = PaymentTestData.Integration(name: "Reserva");

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.PIX, principal, backup));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([principal.Id.Value, backup.Id.Value], result.Output!.Select(integration => integration.Id.Value));
    }

    [Fact]
    public void Given_InactiveFirstIntegration_When_Ranking_Should_FallBackToSecond()
    {
        // Arrange
        var inactive = PaymentTestData.Integration(status: IntegrationStatus.INACTIVE, name: "Principal");
        var backup = PaymentTestData.Integration(name: "Reserva");

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.PIX, inactive, backup));

        // Assert
        Assert.Equal([backup.Id.Value], result.Output!.Select(integration => integration.Id.Value));
    }

    [Fact]
    public void Given_DisabledProvider_When_Ranking_Should_DiscardItsIntegrations()
    {
        // Arrange
        var disabled = PaymentTestData.Integration(PaymentTestData.AbacatePay(ProviderStatus.DISABLED), name: "Desligado");
        var available = PaymentTestData.Integration(PaymentTestData.PixOnlyProvider(), credentialKeys: new HashSet<string> { PaymentTestData.API_KEY, PaymentTestData.ACCOUNT_ID }, name: "Disponível");

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.PIX, disabled, available));

        // Assert
        Assert.Equal([available.Id.Value], result.Output!.Select(integration => integration.Id.Value));
    }

    [Fact]
    public void Given_ProviderThatNoLongerAcceptsTheMethod_When_Ranking_Should_DiscardIt()
    {
        // Arrange
        var pixOnly = PaymentTestData.Integration(PaymentTestData.PixOnlyProvider(), credentialKeys: new HashSet<string> { PaymentTestData.API_KEY, PaymentTestData.ACCOUNT_ID }, name: "Só Pix");
        var abacatePay = PaymentTestData.Integration(name: "AbacatePay");

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.BOLETO, pixOnly, abacatePay));

        // Assert
        Assert.Equal([abacatePay.Id.Value], result.Output!.Select(integration => integration.Id.Value));
    }

    [Fact]
    public void Given_MissingRequiredCredential_When_Ranking_Should_DiscardIt()
    {
        // Arrange
        var incomplete = PaymentTestData.Integration(credentialKeys: new HashSet<string> { PaymentTestData.WEBHOOK_SECRET }, name: "Sem API key");
        var complete = PaymentTestData.Integration(name: "Completa");

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.PIX, incomplete, complete));

        // Assert
        Assert.Equal([complete.Id.Value], result.Output!.Select(integration => integration.Id.Value));
    }

    [Fact]
    public void Given_RouteWithoutIntegrations_When_Ranking_Should_ReturnPaymentMethodUnavailable()
    {
        // Arrange & Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.BOLETO));

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(PaymentIntegrationRanking.PAYMENT_METHOD_UNAVAILABLE_NOTIFICATION_CODE, notification.Code);
    }

    [Fact]
    public void Given_EveryIntegrationDiscarded_When_Ranking_Should_ReturnPaymentMethodUnavailable()
    {
        // Arrange
        var inactive = PaymentTestData.Integration(status: IntegrationStatus.INACTIVE);

        // Act
        var result = PaymentIntegrationRanking.Rank(Route(PaymentMethod.PIX, inactive));

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "PAYMENT_METHOD_UNAVAILABLE");
    }
}
