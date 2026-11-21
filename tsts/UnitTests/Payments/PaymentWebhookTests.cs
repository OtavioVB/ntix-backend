using Domain.BoundedContexts.OrderContext.Enums;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnitTests.Support;

namespace UnitTests.Payments;

/// <summary>Assinatura do webhook (REQ8, RN1) e segredo da integração (RN3), sem banco.</summary>
public sealed class WebhookSignatureTests
{
    private const string PUBLIC_KEY = "chave-publica-de-teste";
    private const string SECRET = "segredo-de-32-caracteres-ou-mais-ok";

    private static readonly byte[] BODY = Encoding.UTF8.GetBytes("{\"id\":\"log_12345\",\"event\":\"transparent.completed\"}");

    private static string Sign(byte[] body, string key)
        => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body));

    [Fact]
    public void Given_SignatureOfTheRawBody_When_Checking_Should_BeValid()
    {
        // Arrange
        var signature = Sign(BODY, PUBLIC_KEY);

        // Act
        var valid = WebhookSignature.IsValid(BODY, signature, PUBLIC_KEY);

        // Assert
        Assert.True(valid);
    }

    [Fact]
    public void Given_BodyChangedAfterSigning_When_Checking_Should_BeInvalid()
    {
        // Arrange
        var signature = Sign(BODY, PUBLIC_KEY);
        var tampered = Encoding.UTF8.GetBytes("{\"id\":\"log_99999\",\"event\":\"transparent.completed\"}");

        // Act
        var valid = WebhookSignature.IsValid(tampered, signature, PUBLIC_KEY);

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void Given_MissingSignature_When_Checking_Should_BeInvalid()
    {
        // Act
        var valid = WebhookSignature.IsValid(BODY, null, PUBLIC_KEY);

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void Given_NoPublicKeyConfigured_When_Checking_Should_FailClosed()
    {
        // Arrange
        var signature = Sign(BODY, string.Empty);

        // Act
        var valid = WebhookSignature.IsValid(BODY, signature, string.Empty);

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void Given_SameSecret_When_Comparing_Should_Match()
        => Assert.True(WebhookSignature.SecretMatches(SECRET, SECRET));

    [Fact]
    public void Given_DifferentSecret_When_Comparing_Should_NotMatch()
        => Assert.False(WebhookSignature.SecretMatches("outro-segredo", SECRET));

    [Fact]
    public void Given_MissingSecretHeader_When_Comparing_Should_NotMatch()
        => Assert.False(WebhookSignature.SecretMatches(null, SECRET));
}

/// <summary>Confirmação do pagamento no domínio (REQ8, RN6, RN7, RN8, RN9, RN10), sem banco.</summary>
public sealed class OrderPaymentConfirmationTests
{
    private static readonly IdValueObject CORRELATION = OrderTestData.CORRELATION_ID;

    [Fact]
    public void Given_TwoAndOneUnits_When_Confirming_Should_IssueOneTicketPerUnitInTheBuyerName()
    {
        // Arrange
        var order = OrderTestData.Create([OrderTestData.Request(2), OrderTestData.Request(1, OrderTestData.VIP_BATCH_ID)], [OrderTestData.Offer(), OrderTestData.VipOffer()]).Output!;

        // Act
        var confirmation = order.ConfirmPayment(OrderTestData.NewPayment(), "Marina Souza", OrderTestData.NOW, CORRELATION);

        // Assert
        Assert.Equal(3, confirmation.Tickets.Count);
        Assert.All(confirmation.Tickets, ticket => Assert.Equal("Marina Souza", ticket.HolderName));
        Assert.All(confirmation.Tickets, ticket => Assert.Equal(TicketStatus.VALID, ticket.Status));
        Assert.Equal(3, confirmation.Tickets.Select(ticket => ticket.Code).Distinct().Count());
    }

    [Fact]
    public void Given_PendingOrderWithinReservation_When_Confirming_Should_BePaidWithoutLateFlag()
    {
        // Arrange
        var order = OrderTestData.NewOrder();

        // Act
        var confirmation = order.ConfirmPayment(OrderTestData.NewPayment(), "Marina Souza", OrderTestData.NOW, CORRELATION);

        // Assert
        Assert.Equal(OrderStatus.PAID, confirmation.Order.Status);
        Assert.Equal(OrderTestData.NOW, confirmation.Order.PaidAt);
        Assert.False(confirmation.PaidAfterExpiration);
    }

    [Fact]
    public void Given_ReservationAlreadyExpired_When_Confirming_Should_HonorAndFlagTheLatePayment()
    {
        // Arrange
        var order = OrderTestData.NewOrder();
        var late = OrderTestData.NOW.AddMinutes(20);

        // Act
        var confirmation = order.ConfirmPayment(OrderTestData.NewPayment(), "Marina Souza", late, CORRELATION);

        // Assert (RN10): o comprador pagou, então o pedido é honrado e sinalizado.
        Assert.Equal(OrderStatus.PAID, confirmation.Order.Status);
        Assert.Equal(2, confirmation.Tickets.Count);
        Assert.True(confirmation.PaidAfterExpiration);
    }

    [Fact]
    public void Given_Confirmation_When_Building_OrderPaidEvent_Should_CarryBusinessIdsOnly()
    {
        // Arrange
        var order = OrderTestData.NewOrder();
        var payment = OrderTestData.NewPayment();

        // Act
        var orderPaid = order.ConfirmPayment(payment, "Marina Souza", OrderTestData.NOW, CORRELATION).Event;

        // Assert (RN8): sem nome, e-mail ou CPF.
        Assert.Equal(order.Id.Value, orderPaid.OrderId);
        Assert.Equal(order.Code.Value, orderPaid.OrderCode);
        Assert.Equal(order.TotalCents, orderPaid.TotalCents);
        Assert.Equal(payment.Id.Value, orderPaid.PaymentId);
        Assert.Equal(payment.IntegrationId.Value, orderPaid.PaymentIntegrationId);
        Assert.Equal("PIX", orderPaid.PaymentMethod);
        Assert.DoesNotContain("Marina", JsonSerializer.Serialize(orderPaid));
    }

    [Fact]
    public void Given_PendingPayment_When_Confirming_Should_BecomePaid()
    {
        // Arrange
        var payment = OrderTestData.NewPayment();

        // Act
        var paid = payment.Confirm(OrderTestData.NOW, OrderTestData.NOW, CORRELATION);

        // Assert
        Assert.Equal(PaymentStatus.PAID, paid.Status);
        Assert.Equal(OrderTestData.NOW, paid.PaidAt);
    }

    [Fact]
    public void Given_PaymentAmountDifferentFromOrder_When_Checking_Should_NotMatch()
    {
        // Arrange
        var payment = OrderTestData.NewPayment();

        // Act & Assert (RN9): o Pix de 24000 não confere com um pedido de 12000.
        Assert.False(payment.HasAmount(12000));
        Assert.True(payment.HasAmount(24000));
    }
}

/// <summary>Código público do ingresso (RN7): 12 caracteres do alfabeto sem os ambíguos.</summary>
public sealed class TicketCodeValueObjectTests
{
    [Fact]
    public void Given_Generation_When_Creating_Should_HaveTwelveCharactersFromTheAlphabet()
    {
        // Act
        var code = TicketCodeValueObject.Generate().Value;

        // Assert
        Assert.Equal(12, code.Length);
        Assert.All(code, character => Assert.Contains(character, TicketCodeValueObject.ALPHABET));
    }

    [Fact]
    public void Given_ManyGenerations_When_Comparing_Should_BeUnique()
    {
        // Act
        var codes = Enumerable.Range(0, 2000).Select(_ => TicketCodeValueObject.Generate().Value).ToArray();

        // Assert
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}
