using Domain.ValueObjects;
using System.Globalization;

namespace UnitTests.ValueObjects;

public sealed class AuditableInfoValueObjectTests
{
    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "customer-portal";
    private const string EXECUTION_USER = "user-123";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private static string FormatRequestedAt(DateTime requestedAt)
        => requestedAt.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, CultureInfo.InvariantCulture);

    [Fact]
    public void Given_AllValidHeaders_When_Building_Should_ExposeAllValues()
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

        // Assert
        Assert.True(auditableInfo.IsValid);
        Assert.True(auditableInfo.MethodResult.IsSuccess);
        Assert.Equal(Guid.Parse(CORRELATION_ID), auditableInfo.CorrelationId);
        Assert.Equal(ORIGIN_SERVICE, auditableInfo.OriginService);
        Assert.Equal(EXECUTION_USER, auditableInfo.ExecutionUser);
        Assert.Equal(new DateTime(2026, 10, 10, 3, 7, 15, 123, DateTimeKind.Utc), auditableInfo.RequestedAt);
        Assert.Equal(DateTimeKind.Utc, auditableInfo.RequestedAt.Kind);
    }

    [Fact]
    public void Given_TextHeadersWithSpaces_When_Building_Should_TrimValues()
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, $"  {ORIGIN_SERVICE} ", $" {EXECUTION_USER}  ", REQUESTED_AT);

        // Assert
        Assert.Equal(ORIGIN_SERVICE, auditableInfo.OriginService);
        Assert.Equal(EXECUTION_USER, auditableInfo.ExecutionUser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Given_NoExecutionUser_When_Building_Should_BeValidWithoutUser(string? executionUser)
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, executionUser, REQUESTED_AT);

        // Assert
        Assert.True(auditableInfo.IsValid);
        Assert.Null(auditableInfo.ExecutionUser);
    }

    [Fact]
    public void Given_NoRequiredHeaders_When_Building_Should_ReportAllAtOnce()
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(null, " ", null, "");

        // Assert
        Assert.False(auditableInfo.IsValid);
        Assert.Collection(
            auditableInfo.MethodResult.Notifications,
            notification => Assert.Equal("AUDITABLE_CORRELATION_ID_REQUIRED", notification.Code),
            notification => Assert.Equal("AUDITABLE_ORIGIN_SERVICE_REQUIRED", notification.Code),
            notification => Assert.Equal("AUDITABLE_REQUESTED_AT_REQUIRED", notification.Code));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void Given_InvalidCorrelationId_When_Building_Should_ReturnInvalidNotification(string correlationId)
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(correlationId, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

        // Assert
        Assert.False(auditableInfo.IsValid);
        var notification = Assert.Single(auditableInfo.MethodResult.Notifications);
        Assert.Equal("AUDITABLE_CORRELATION_ID_INVALID", notification.Code);
    }

    [Fact]
    public void Given_OriginServiceWithMaximumLength_When_Building_Should_BeValid()
    {
        // Arrange
        var originService = new string('s', AuditableInfoValueObject.ORIGIN_SERVICE_MAX_LENGTH);

        // Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, originService, EXECUTION_USER, REQUESTED_AT);

        // Assert
        Assert.True(auditableInfo.IsValid);
    }

    [Fact]
    public void Given_OriginServiceOverMaximumLength_When_Building_Should_ReturnLengthNotification()
    {
        // Arrange
        var originService = new string('s', AuditableInfoValueObject.ORIGIN_SERVICE_MAX_LENGTH + 1);

        // Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, originService, EXECUTION_USER, REQUESTED_AT);

        // Assert
        Assert.False(auditableInfo.IsValid);
        var notification = Assert.Single(auditableInfo.MethodResult.Notifications);
        Assert.Equal("AUDITABLE_ORIGIN_SERVICE_LENGTH_INVALID", notification.Code);
    }

    [Fact]
    public void Given_ExecutionUserOverMaximumLength_When_Building_Should_ReturnLengthNotification()
    {
        // Arrange
        var executionUser = new string('u', AuditableInfoValueObject.EXECUTION_USER_MAX_LENGTH + 1);

        // Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, executionUser, REQUESTED_AT);

        // Assert
        Assert.False(auditableInfo.IsValid);
        var notification = Assert.Single(auditableInfo.MethodResult.Notifications);
        Assert.Equal("AUDITABLE_EXECUTION_USER_LENGTH_INVALID", notification.Code);
    }

    [Theory]
    [InlineData("2026-10-10T03:07:15Z")]
    [InlineData("2026-10-10T03:07:15.123")]
    [InlineData("2026-10-10T03:07:15.123-03:00")]
    [InlineData("10/10/2026 03:07:15")]
    public void Given_RequestedAtWithInvalidFormat_When_Building_Should_ReturnFormatNotification(string requestedAt)
    {
        // Arrange & Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, requestedAt);

        // Assert
        Assert.False(auditableInfo.IsValid);
        var notification = Assert.Single(auditableInfo.MethodResult.Notifications);
        Assert.Equal("AUDITABLE_REQUESTED_AT_FORMAT_INVALID", notification.Code);
    }

    [Fact]
    public void Given_RequestedAtWithinToleranceInTheFuture_When_Building_Should_BeValid()
    {
        // Arrange
        var requestedAt = FormatRequestedAt(DateTime.UtcNow.AddMinutes(AuditableInfoValueObject.REQUESTED_AT_MAX_CLOCK_SKEW_IN_MINUTES - 1));

        // Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, requestedAt);

        // Assert
        Assert.True(auditableInfo.IsValid);
    }

    [Fact]
    public void Given_RequestedAtBeyondToleranceInTheFuture_When_Building_Should_ReturnInFutureNotification()
    {
        // Arrange
        var requestedAt = FormatRequestedAt(DateTime.UtcNow.AddMinutes(AuditableInfoValueObject.REQUESTED_AT_MAX_CLOCK_SKEW_IN_MINUTES + 1));

        // Act
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, requestedAt);

        // Assert
        Assert.False(auditableInfo.IsValid);
        var notification = Assert.Single(auditableInfo.MethodResult.Notifications);
        Assert.Equal("AUDITABLE_REQUESTED_AT_IN_FUTURE", notification.Code);
    }

    [Fact]
    public void Given_InvalidAuditableInfo_When_GettingValues_Should_ThrowInvalidOperationException()
    {
        // Arrange
        var auditableInfo = AuditableInfoValueObject.Build(null, null, null, null);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => auditableInfo.CorrelationId);
        Assert.Throws<InvalidOperationException>(() => auditableInfo.OriginService);
        Assert.Throws<InvalidOperationException>(() => auditableInfo.ExecutionUser);
        Assert.Throws<InvalidOperationException>(() => auditableInfo.RequestedAt);
    }
}
