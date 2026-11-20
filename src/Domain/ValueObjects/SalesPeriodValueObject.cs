using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Globalization;

namespace Domain.ValueObjects;

/// <summary>
/// Janela de venda do lote, em UTC (REQ10, RN5): início antes do fim. Que o fim não passe do fim do evento depende do
/// evento, e fica na entidade do lote.
/// </summary>
public readonly struct SalesPeriodValueObject
{
    public bool IsValid { get; }

    private readonly DateTime _startsAt;
    private readonly DateTime _endsAt;

    public DateTime StartsAt => IsValid ? _startsAt : throw InvalidValueObject();
    public DateTime EndsAt => IsValid ? _endsAt : throw InvalidValueObject();

    public MethodResult MethodResult { get; }

    private SalesPeriodValueObject(bool isValid, MethodResult methodResult, DateTime startsAt = default, DateTime endsAt = default)
    {
        IsValid = isValid;
        _startsAt = startsAt;
        _endsAt = endsAt;
        MethodResult = methodResult;
    }

    public const string BATCH_SALES_PERIOD_INVALID_NOTIFICATION_CODE = "BATCH_SALES_PERIOD_INVALID";
    private const string BATCH_SALES_PERIOD_INVALID_NOTIFICATION_MESSAGE = "Informe o início e o fim da venda do lote: o fim deve ser depois do início.";

    /// <summary>Datas como vieram na requisição (ISO 8601); ilegível vira a notificação da janela. Sem fuso, é UTC.</summary>
    public static SalesPeriodValueObject Build(string? startsAt, string? endsAt)
        => Build(TryParse(startsAt), TryParse(endsAt));

    public static SalesPeriodValueObject Build(DateTime? startsAt, DateTime? endsAt)
    {
        if (startsAt is null || endsAt is null)
            return Invalid();

        var start = DateTime.SpecifyKind(startsAt.Value.Kind == DateTimeKind.Local ? startsAt.Value.ToUniversalTime() : startsAt.Value, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(endsAt.Value.Kind == DateTimeKind.Local ? endsAt.Value.ToUniversalTime() : endsAt.Value, DateTimeKind.Utc);

        if (end <= start)
            return Invalid();

        return new SalesPeriodValueObject(isValid: true, startsAt: start, endsAt: end, methodResult: MethodResult.Success());
    }

    private static DateTime? TryParse(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    private static SalesPeriodValueObject Invalid()
        => new(isValid: false, methodResult: MethodResult.Error([Notification.Error(BATCH_SALES_PERIOD_INVALID_NOTIFICATION_CODE, BATCH_SALES_PERIOD_INVALID_NOTIFICATION_MESSAGE)]));

    private static InvalidOperationException InvalidValueObject()
        => new("It is not possible to get sales period because value object is invalid");
}
