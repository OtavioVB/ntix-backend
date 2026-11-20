using Application.UseCases.Base.Payments;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.BoundedContexts.PaymentContext.Services;
using UnitTests.Support;

namespace UnitTests.Payments;

/// <summary>Janela do Pix dentro da reserva (REQ7, RN5), descrição da cobrança (RN8) e a cobrança do pedido.</summary>
public sealed class PixWindowAndDescriptionTests
{
    private static readonly DateTime NOW = OrderTestData.NOW;
    private static readonly TimeSpan GRACE = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MINIMUM = TimeSpan.FromSeconds(300);

    [Fact]
    public void Given_FreshReservation_When_Calculating_Should_EndTheGraceBeforeTheReservation()
    {
        // Arrange: pedido criado agora, com 15 minutos de reserva.
        var window = PixWindow.Calculate(NOW.AddMinutes(15), GRACE, MINIMUM, NOW);

        // Assert
        Assert.True(window.Fits);
        Assert.Equal(NOW.AddMinutes(13), window.ExpiresAt);
        Assert.Equal(13 * 60, window.ExpiresInSeconds);
    }

    [Theory]
    [InlineData(420, true)]
    [InlineData(419, false)]
    [InlineData(0, false)]
    [InlineData(-60, false)]
    public void Given_RemainingReservation_When_Calculating_Should_NeedSevenMinutes(int remainingSeconds, bool fits)
    {
        // Arrange: 7 minutos de reserva = 120 s de folga + 300 s de mínimo do fornecedor.
        var window = PixWindow.Calculate(NOW.AddSeconds(remainingSeconds), GRACE, MINIMUM, NOW);

        // Assert
        Assert.Equal(fits, window.Fits);
        Assert.True(window.ExpiresInSeconds >= 0);
    }

    [Fact]
    public void Given_FractionalSeconds_When_Calculating_Should_FloorTheValidityRequested()
    {
        // Arrange & Act
        var window = PixWindow.Calculate(NOW.AddSeconds(900.9), GRACE, MINIMUM, NOW);

        // Assert
        Assert.Equal(780, window.ExpiresInSeconds);
    }

    [Fact]
    public void Given_OrderAndEvent_When_BuildingDescription_Should_JoinCodeAndTitle()
        => Assert.Equal("Pedido NX7K2QH9MP - Festival Horizonte", PixChargeDescription.Build("NX7K2QH9MP", "Festival Horizonte"));

    [Fact]
    public void Given_TitleWithEmojiAndControlCharacters_When_BuildingDescription_Should_KeepOnlyPrintableText()
        => Assert.Equal("Pedido NX7K2QH9MP - Noite de Vinil  ao vivo", PixChargeDescription.Build("NX7K2QH9MP", "Noite de Vinil \U0001F3B6\t​ ao vivo"));

    [Fact]
    public void Given_VeryLongTitle_When_BuildingDescription_Should_TruncateTo140()
    {
        // Arrange & Act
        var description = PixChargeDescription.Build("NX7K2QH9MP", new string('a', 300));

        // Assert
        Assert.Equal(PixChargeDescription.MAX_LENGTH, description.Length);
        Assert.StartsWith("Pedido NX7K2QH9MP - aaa", description);
    }

    [Fact]
    public void Given_TitleThatEndsInSpaceAtTheLimit_When_BuildingDescription_Should_NotEndWithSpace()
    {
        // Arrange: o corte cai logo depois de um espaço.
        var title = new string('a', 140 - "Pedido NX7K2QH9MP - ".Length - 1) + " bbbb";

        // Act
        var description = PixChargeDescription.Build("NX7K2QH9MP", title);

        // Assert
        Assert.False(description.EndsWith(' '));
    }

    [Fact]
    public void Given_NewPix_When_CreatingPayment_Should_StartPendingWithTheChargeData()
    {
        // Arrange & Act
        var payment = OrderTestData.NewPayment();

        // Assert
        Assert.Equal(PaymentStatus.PENDING, payment.Status);
        Assert.Equal(PaymentMethod.PIX, payment.Method);
        Assert.Equal(24000, payment.AmountCents);
        Assert.Equal("pix_char_abc123", payment.ProviderReference);
    }

    [Theory]
    [InlineData(-1, PaymentStatus.PENDING, true)]
    [InlineData(0, PaymentStatus.EXPIRED, false)]
    [InlineData(1, PaymentStatus.EXPIRED, false)]
    public void Given_PendingPayment_When_GettingEffectiveStatus_Should_ExpireWithThePix(int secondsAfterExpiry, PaymentStatus expected, bool validPending)
    {
        // Arrange
        var payment = OrderTestData.NewPayment();
        var at = payment.ExpiresAt.Value.AddSeconds(secondsAfterExpiry);

        // Act & Assert
        Assert.Equal(expected, payment.GetEffectiveStatus(at));
        Assert.Equal(validPending, payment.IsPendingAndValid(at));
    }

    [Fact]
    public void Given_PaidPayment_When_GettingEffectiveStatus_Should_StayPaid()
    {
        // Arrange
        var payment = OrderTestData.NewPayment() with { Status = PaymentStatus.PAID };

        // Act & Assert
        Assert.Equal(PaymentStatus.PAID, payment.GetEffectiveStatus(payment.ExpiresAt.Value.AddDays(1)));
        Assert.False(payment.IsPendingAndValid(payment.CreatedAt.Value));
    }

    [Fact]
    public void Given_PendingPayment_When_MarkingExpired_Should_ChangeStatusAndAudit()
    {
        // Arrange
        var payment = OrderTestData.NewPayment();
        var later = NOW.AddMinutes(20);

        // Act
        var expired = payment.MarkExpired(later, OrderTestData.CORRELATION_ID);

        // Assert
        Assert.Equal(PaymentStatus.EXPIRED, expired.Status);
        Assert.Equal(later, expired.UpdatedAt.Value);
    }
}
