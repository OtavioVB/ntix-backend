using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Authentication;

public sealed class LoginAttemptLimiterTests
{
    private readonly FakeTimeProvider _timeProvider = new(ProducerSessionTestData.NOW);
    private readonly LoginAttemptLimiter _limiter;

    private static readonly EmailValueObject EMAIL = EmailValueObject.Build("ana@produtora.com.br");

    public LoginAttemptLimiterTests()
    {
        _limiter = new LoginAttemptLimiter(new LoginAttemptConfiguration(PermitLimit: 10, WindowMinutes: 15), _timeProvider);
    }

    private void AcquireTimes(EmailValueObject email, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
            Assert.True(_limiter.TryAcquire(email));
    }

    [Fact]
    public void Given_NineAttempts_When_TryingTheTenth_Should_Accept()
    {
        // Arrange
        AcquireTimes(EMAIL, 9);

        // Act
        var accepted = _limiter.TryAcquire(EMAIL);

        // Assert
        Assert.True(accepted);
    }

    [Fact]
    public void Given_TenAttempts_When_TryingTheEleventh_Should_RefuseUntilTheWindowEnds()
    {
        // Arrange
        AcquireTimes(EMAIL, 10);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Act
        var accepted = _limiter.TryAcquire(EMAIL);
        var retryAfter = _limiter.GetRetryAfterSeconds(EMAIL);

        // Assert
        Assert.False(accepted);
        Assert.Equal(600, retryAfter);
    }

    [Fact]
    public void Given_WindowFull_When_15MinutesPassFromTheFirstAttempt_Should_AcceptAgain()
    {
        // Arrange
        AcquireTimes(EMAIL, 10);
        _timeProvider.Advance(TimeSpan.FromMinutes(15).Subtract(TimeSpan.FromSeconds(1)));
        Assert.False(_limiter.TryAcquire(EMAIL));

        // Act
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        var accepted = _limiter.TryAcquire(EMAIL);

        // Assert
        Assert.True(accepted);
        Assert.Equal(0, _limiter.GetRetryAfterSeconds(EMAIL));
    }

    [Fact]
    public void Given_OneEmailBlocked_When_AnotherEmailTries_Should_HaveItsOwnCounter()
    {
        // Arrange
        AcquireTimes(EMAIL, 10);

        // Act
        var accepted = _limiter.TryAcquire(EmailValueObject.Build("outra@produtora.com.br"));

        // Assert
        Assert.True(accepted);
        Assert.False(_limiter.TryAcquire(EMAIL));
    }

    [Fact]
    public void Given_SameEmailWithDifferentCase_When_Trying_Should_ShareTheCounter()
    {
        // Arrange
        AcquireTimes(EMAIL, 10);

        // Act
        var accepted = _limiter.TryAcquire(EmailValueObject.Build("  ANA@Produtora.com.br "));

        // Assert
        Assert.False(accepted);
    }

    [Fact]
    public void Given_ManyEmailsInTheOldWindow_When_ANewWindowStarts_Should_StillCountFromZero()
    {
        // Arrange
        for (var index = 0; index < 50; index++)
            _limiter.TryAcquire(EmailValueObject.Build($"pessoa{index}@produtora.com.br"));

        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Act
        AcquireTimes(EmailValueObject.Build("pessoa1@produtora.com.br"), 10);

        // Assert
        Assert.False(_limiter.TryAcquire(EmailValueObject.Build("pessoa1@produtora.com.br")));
    }
}
