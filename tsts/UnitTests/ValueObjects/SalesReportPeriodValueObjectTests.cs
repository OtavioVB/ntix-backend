using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

/// <summary>Período do gráfico de vendas por dia (REQ14): datas de Brasília, padrão de 30 dias e limite de 366.</summary>
public sealed class SalesReportPeriodValueObjectTests
{
    // 21/11/2026 às 01h em UTC ainda é 20/11 em Brasília.
    private static readonly DateTime NOW = new(2026, 11, 21, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Given_NoDates_When_Building_Should_BeTheLast30DaysUntilTodayInBrasilia()
    {
        // Arrange & Act
        var period = SalesReportPeriodValueObject.Build(null, null, NOW);

        // Assert
        Assert.True(period.IsValid);
        Assert.Equal(new DateOnly(2026, 11, 20), period.To);
        Assert.Equal(new DateOnly(2026, 10, 22), period.From);
        Assert.Equal(30, period.Days.Count());
    }

    [Fact]
    public void Given_ExplicitDates_When_Building_Should_ConvertTheLocalDaysToAHalfOpenUtcInterval()
    {
        // Arrange & Act
        var period = SalesReportPeriodValueObject.Build("2026-11-01", "2026-11-03", NOW);

        // Assert
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), period.StartsAtUtc);
        Assert.Equal(new DateTime(2026, 11, 4, 3, 0, 0, DateTimeKind.Utc), period.EndsBeforeUtc);
        Assert.Equal(3, period.Days.Count());
    }

    [Fact]
    public void Given_OnlyTheStart_When_Building_Should_Complete30Days()
    {
        // Arrange & Act
        var period = SalesReportPeriodValueObject.Build("2026-01-01", null, NOW);

        // Assert
        Assert.Equal(new DateOnly(2026, 1, 30), period.To);
    }

    [Fact]
    public void Given_Exactly366Days_When_Building_Should_BeValid()
    {
        // Arrange & Act
        var period = SalesReportPeriodValueObject.Build("2026-01-01", "2027-01-01", NOW);

        // Assert
        Assert.True(period.IsValid);
    }

    [Theory]
    [InlineData("2026-01-01", "2027-01-02")]
    [InlineData("2026-11-03", "2026-11-01")]
    [InlineData("01/11/2026", null)]
    [InlineData("2026-02-30", null)]
    [InlineData(null, "amanhã")]
    public void Given_InvalidPeriod_When_Building_Should_ReportSalesPeriodInvalid(string? from, string? to)
    {
        // Arrange & Act
        var period = SalesReportPeriodValueObject.Build(from, to, NOW);

        // Assert
        Assert.False(period.IsValid);
        Assert.Equal("SALES_PERIOD_INVALID", Assert.Single(period.MethodResult.Notifications).Code);
    }
}
