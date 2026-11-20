using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Período do evento, em UTC: início, fim e, opcionalmente, a abertura dos portões.
/// RN5: fim depois do início e duração de no máximo 30 dias. RN6: portões abrem até 12 horas antes do início, nunca depois.
/// "Início no futuro" depende do relógio e da operação (só na criação), por isso fica na entidade.
/// </summary>
public readonly struct EventPeriodValueObject
{
    public bool IsValid { get; }

    private readonly DateTime _startsAt;
    private readonly DateTime _endsAt;
    private readonly DateTime? _doorsOpenAt;

    public DateTime StartsAt => IsValid ? _startsAt : throw InvalidValueObject();
    public DateTime EndsAt => IsValid ? _endsAt : throw InvalidValueObject();
    public DateTime? DoorsOpenAt => IsValid ? _doorsOpenAt : throw InvalidValueObject();

    public MethodResult MethodResult { get; }

    private EventPeriodValueObject(bool isValid, MethodResult methodResult, DateTime startsAt = default, DateTime endsAt = default, DateTime? doorsOpenAt = null)
    {
        IsValid = isValid;
        _startsAt = startsAt;
        _endsAt = endsAt;
        _doorsOpenAt = doorsOpenAt;
        MethodResult = methodResult;
    }

    public static readonly TimeSpan MAX_DURATION = TimeSpan.FromDays(30);
    public static readonly TimeSpan MAX_DOORS_ADVANCE = TimeSpan.FromHours(12);

    private const string EVENT_PERIOD_INVALID_NOTIFICATION_CODE = "EVENT_PERIOD_INVALID";
    private const string EVENT_PERIOD_INVALID_NOTIFICATION_MESSAGE = "Informe o início e o fim do evento: o fim deve ser depois do início, com duração de até 30 dias.";

    private const string EVENT_DOORS_OPEN_INVALID_NOTIFICATION_CODE = "EVENT_DOORS_OPEN_INVALID";
    private const string EVENT_DOORS_OPEN_INVALID_NOTIFICATION_MESSAGE = "A abertura dos portões deve ser até o início do evento e no máximo 12 horas antes.";

    /// <summary>
    /// Recebe as datas como vieram na requisição (ISO 8601). Data ilegível vira notificação do próprio campo, em vez de
    /// um erro do framework: início ou fim ilegível é período inválido; abertura ilegível é abertura inválida.
    /// </summary>
    public static EventPeriodValueObject Build(string? startsAt, string? endsAt, string? doorsOpenAt)
    {
        var start = TryParse(startsAt);
        var end = TryParse(endsAt);
        var doors = TryParse(doorsOpenAt);

        if (!string.IsNullOrWhiteSpace(doorsOpenAt) && doors is null)
            return Invalid(EVENT_DOORS_OPEN_INVALID_NOTIFICATION_CODE, EVENT_DOORS_OPEN_INVALID_NOTIFICATION_MESSAGE);

        return Build(start, end, doors);
    }

    private static DateTime? TryParse(string? value)
        => DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    public static EventPeriodValueObject Build(DateTime? startsAt, DateTime? endsAt, DateTime? doorsOpenAt)
    {
        if (startsAt is null || endsAt is null)
            return Invalid(EVENT_PERIOD_INVALID_NOTIFICATION_CODE, EVENT_PERIOD_INVALID_NOTIFICATION_MESSAGE);

        var start = ToUtc(startsAt.Value);
        var end = ToUtc(endsAt.Value);
        var doors = doorsOpenAt is null ? (DateTime?)null : ToUtc(doorsOpenAt.Value);

        var notifications = new List<Notification>();

        if (end <= start || end - start > MAX_DURATION)
            notifications.Add(Notification.Error(EVENT_PERIOD_INVALID_NOTIFICATION_CODE, EVENT_PERIOD_INVALID_NOTIFICATION_MESSAGE));

        if (doors is not null && (doors > start || start - doors > MAX_DOORS_ADVANCE))
            notifications.Add(Notification.Error(EVENT_DOORS_OPEN_INVALID_NOTIFICATION_CODE, EVENT_DOORS_OPEN_INVALID_NOTIFICATION_MESSAGE));

        if (notifications.Count > 0)
            return new EventPeriodValueObject(isValid: false, methodResult: MethodResult.Error([.. notifications]));

        return new EventPeriodValueObject(isValid: true, startsAt: start, endsAt: end, doorsOpenAt: doors, methodResult: MethodResult.Success());
    }

    // Data sem fuso (Unspecified) é tratada como UTC, como a API documenta; com fuso, é convertida.
    private static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static EventPeriodValueObject Invalid(string code, string message)
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(code: code, message: message)]));

    private static InvalidOperationException InvalidValueObject()
        => new("It is not possible to get event period because value object is invalid");
}
