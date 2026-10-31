using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using System.Globalization;

namespace Domain.ValueObjects;

public readonly struct AuditableInfoValueObject
{
    public const string CORRELATION_ID_HEADER_KEY = "X-Correlation-Id";
    public const string ORIGIN_SERVICE_HEADER_KEY = "X-Origin-Service";
    public const string EXECUTION_USER_HEADER_KEY = "X-Execution-User";
    public const string REQUESTED_AT_HEADER_KEY = "X-Requested-At";

    public const string REQUESTED_AT_FORMAT = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public const int ORIGIN_SERVICE_MAX_LENGTH = 100;
    public const int EXECUTION_USER_MAX_LENGTH = 255;
    public const int REQUESTED_AT_MAX_CLOCK_SKEW_IN_MINUTES = 5;

    public bool IsValid { get; }

    private readonly Guid _correlationId;
    public Guid CorrelationId => IsValid ? _correlationId : throw BuildInvalidValueException();

    private readonly string? _originService;
    public string OriginService => IsValid ? _originService! : throw BuildInvalidValueException();

    private readonly string? _executionUser;
    public string? ExecutionUser => IsValid ? _executionUser : throw BuildInvalidValueException();

    private readonly DateTime _requestedAt;
    public DateTime RequestedAt => IsValid ? _requestedAt : throw BuildInvalidValueException();

    public MethodResult MethodResult { get; }

    private AuditableInfoValueObject(bool isValid, MethodResult methodResult, Guid correlationId = default, string? originService = null, string? executionUser = null, DateTime requestedAt = default)
    {
        IsValid = isValid;
        _correlationId = correlationId;
        _originService = originService;
        _executionUser = executionUser;
        _requestedAt = requestedAt;
        MethodResult = methodResult;
    }

    private const string CORRELATION_ID_REQUIRED_NOTIFICATION_CODE = "AUDITABLE_CORRELATION_ID_REQUIRED";
    private const string CORRELATION_ID_REQUIRED_NOTIFICATION_MESSAGE = $"O header {CORRELATION_ID_HEADER_KEY} é obrigatório.";

    private const string CORRELATION_ID_INVALID_NOTIFICATION_CODE = "AUDITABLE_CORRELATION_ID_INVALID";
    private const string CORRELATION_ID_INVALID_NOTIFICATION_MESSAGE = $"O header {CORRELATION_ID_HEADER_KEY} deve ser um UUID válido, diferente de zero e do maior UUID possível.";

    private const string ORIGIN_SERVICE_REQUIRED_NOTIFICATION_CODE = "AUDITABLE_ORIGIN_SERVICE_REQUIRED";
    private const string ORIGIN_SERVICE_REQUIRED_NOTIFICATION_MESSAGE = $"O header {ORIGIN_SERVICE_HEADER_KEY} é obrigatório.";

    private const string ORIGIN_SERVICE_LENGTH_INVALID_NOTIFICATION_CODE = "AUDITABLE_ORIGIN_SERVICE_LENGTH_INVALID";
    private const string ORIGIN_SERVICE_LENGTH_INVALID_NOTIFICATION_MESSAGE = $"O header {ORIGIN_SERVICE_HEADER_KEY} deve ter no máximo 100 caracteres.";

    private const string EXECUTION_USER_LENGTH_INVALID_NOTIFICATION_CODE = "AUDITABLE_EXECUTION_USER_LENGTH_INVALID";
    private const string EXECUTION_USER_LENGTH_INVALID_NOTIFICATION_MESSAGE = $"O header {EXECUTION_USER_HEADER_KEY}, quando enviado, deve ter no máximo 255 caracteres.";

    private const string REQUESTED_AT_REQUIRED_NOTIFICATION_CODE = "AUDITABLE_REQUESTED_AT_REQUIRED";
    private const string REQUESTED_AT_REQUIRED_NOTIFICATION_MESSAGE = $"O header {REQUESTED_AT_HEADER_KEY} é obrigatório.";

    private const string REQUESTED_AT_FORMAT_INVALID_NOTIFICATION_CODE = "AUDITABLE_REQUESTED_AT_FORMAT_INVALID";
    private const string REQUESTED_AT_FORMAT_INVALID_NOTIFICATION_MESSAGE = $"O header {REQUESTED_AT_HEADER_KEY} deve estar em ISO 8601 UTC com milissegundos (ex.: 2026-10-31T12:00:00.123Z).";

    private const string REQUESTED_AT_IN_FUTURE_NOTIFICATION_CODE = "AUDITABLE_REQUESTED_AT_IN_FUTURE";
    private const string REQUESTED_AT_IN_FUTURE_NOTIFICATION_MESSAGE = $"O header {REQUESTED_AT_HEADER_KEY} não pode estar mais de 5 minutos no futuro.";

    public static AuditableInfoValueObject Build(string? correlationId, string? originService, string? executionUser, string? requestedAt)
    {
        const int MAX_POSSIBLE_NOTIFICATIONS = 4;
        var notifications = new List<INotification>(MAX_POSSIBLE_NOTIFICATIONS);

        var parsedCorrelationId = ParseCorrelationId(correlationId, notifications);
        var normalizedOriginService = NormalizeOriginService(originService, notifications);
        var normalizedExecutionUser = NormalizeExecutionUser(executionUser, notifications);
        var parsedRequestedAt = ParseRequestedAt(requestedAt, notifications);

        if (notifications.HasAnyNotifications())
        {
            return new AuditableInfoValueObject(
                isValid: false,
                methodResult: MethodResult.Error(notifications.ToArray()));
        }

        return new AuditableInfoValueObject(
            isValid: true,
            correlationId: parsedCorrelationId,
            originService: normalizedOriginService,
            executionUser: normalizedExecutionUser,
            requestedAt: parsedRequestedAt,
            methodResult: MethodResult.Success());
    }

    private static Guid ParseCorrelationId(string? correlationId, List<INotification> notifications)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            notifications.Add(Notification.Error(
                code: CORRELATION_ID_REQUIRED_NOTIFICATION_CODE,
                message: CORRELATION_ID_REQUIRED_NOTIFICATION_MESSAGE));

            return default;
        }

        if (!Guid.TryParse(correlationId, out var parsed) || parsed == Guid.Empty || parsed == Guid.AllBitsSet)
        {
            notifications.Add(Notification.Error(
                code: CORRELATION_ID_INVALID_NOTIFICATION_CODE,
                message: CORRELATION_ID_INVALID_NOTIFICATION_MESSAGE));

            return default;
        }

        return parsed;
    }

    private static string? NormalizeOriginService(string? originService, List<INotification> notifications)
    {
        var normalized = originService?.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            notifications.Add(Notification.Error(
                code: ORIGIN_SERVICE_REQUIRED_NOTIFICATION_CODE,
                message: ORIGIN_SERVICE_REQUIRED_NOTIFICATION_MESSAGE));
        }
        else if (normalized.Length > ORIGIN_SERVICE_MAX_LENGTH)
        {
            notifications.Add(Notification.Error(
                code: ORIGIN_SERVICE_LENGTH_INVALID_NOTIFICATION_CODE,
                message: ORIGIN_SERVICE_LENGTH_INVALID_NOTIFICATION_MESSAGE));
        }

        return normalized;
    }

    private static string? NormalizeExecutionUser(string? executionUser, List<INotification> notifications)
    {
        var normalized = executionUser?.Trim();

        if (string.IsNullOrEmpty(normalized))
            return null;

        if (normalized.Length > EXECUTION_USER_MAX_LENGTH)
        {
            notifications.Add(Notification.Error(
                code: EXECUTION_USER_LENGTH_INVALID_NOTIFICATION_CODE,
                message: EXECUTION_USER_LENGTH_INVALID_NOTIFICATION_MESSAGE));
        }

        return normalized;
    }

    private static DateTime ParseRequestedAt(string? requestedAt, List<INotification> notifications)
    {
        if (string.IsNullOrWhiteSpace(requestedAt))
        {
            notifications.Add(Notification.Error(
                code: REQUESTED_AT_REQUIRED_NOTIFICATION_CODE,
                message: REQUESTED_AT_REQUIRED_NOTIFICATION_MESSAGE));

            return default;
        }

        var isValidFormat = DateTime.TryParseExact(
            requestedAt,
            REQUESTED_AT_FORMAT,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed);

        if (!isValidFormat)
        {
            notifications.Add(Notification.Error(
                code: REQUESTED_AT_FORMAT_INVALID_NOTIFICATION_CODE,
                message: REQUESTED_AT_FORMAT_INVALID_NOTIFICATION_MESSAGE));

            return default;
        }

        if (parsed > DateTime.UtcNow.AddMinutes(REQUESTED_AT_MAX_CLOCK_SKEW_IN_MINUTES))
        {
            notifications.Add(Notification.Error(
                code: REQUESTED_AT_IN_FUTURE_NOTIFICATION_CODE,
                message: REQUESTED_AT_IN_FUTURE_NOTIFICATION_MESSAGE));
        }

        return parsed;
    }

    private static InvalidOperationException BuildInvalidValueException()
        => new("It is not possible to get auditable info because value object is invalid");
}
