using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Globalization;

namespace Domain.ValueObjects;

/// <summary>
/// Período do gráfico de vendas por dia (REQ14): datas <c>yyyy-MM-dd</c> no fuso de Brasília, inclusivas.
/// Sem datas, os últimos <see cref="DEFAULT_DAYS"/> dias até hoje; com uma só, a outra ponta completa o padrão.
/// </summary>
public readonly struct SalesReportPeriodValueObject
{
    public bool IsValid { get; }

    private readonly DateOnly _from;
    private readonly DateOnly _to;

    public DateOnly From => IsValid
        ? _from
        : throw new InvalidOperationException("It is not possible to get SalesReportPeriod because value object is invalid");

    public DateOnly To => IsValid
        ? _to
        : throw new InvalidOperationException("It is not possible to get SalesReportPeriod because value object is invalid");

    public MethodResult MethodResult { get; }

    public const int DEFAULT_DAYS = 30;
    public const int MAX_DAYS = 366;
    public const string DATE_FORMAT = "yyyy-MM-dd";

    /// <summary>Fuso das datas do relatório: o dia do produtor, não o UTC.</summary>
    public static readonly TimeZoneInfo TIME_ZONE = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public const string SALES_PERIOD_INVALID_NOTIFICATION_CODE = "SALES_PERIOD_INVALID";
    private const string SALES_PERIOD_INVALID_NOTIFICATION_MESSAGE = "Informe o período com datas válidas (AAAA-MM-DD), o início até o fim e no máximo 366 dias.";

    private SalesReportPeriodValueObject(bool isValid, MethodResult methodResult, DateOnly from = default, DateOnly to = default)
    {
        IsValid = isValid;
        MethodResult = methodResult;
        _from = from;
        _to = to;
    }

    public static SalesReportPeriodValueObject Build(string? from, string? to, DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TIME_ZONE));

        var hasFrom = !string.IsNullOrEmpty(from);
        var hasTo = !string.IsNullOrEmpty(to);

        DateOnly parsedFrom = default;
        DateOnly parsedTo = default;

        if ((hasFrom && !TryParse(from, out parsedFrom)) || (hasTo && !TryParse(to, out parsedTo)))
            return Invalid();

        // Sem o fim: com o início, completa o padrão para frente; sem nenhum, termina hoje.
        var end = today;

        if (hasTo)
            end = parsedTo;
        else if (hasFrom)
            end = parsedFrom.AddDays(DEFAULT_DAYS - 1);

        var start = hasFrom ? parsedFrom : end.AddDays(-(DEFAULT_DAYS - 1));

        if (start > end || end.DayNumber - start.DayNumber + 1 > MAX_DAYS)
            return Invalid();

        return new SalesReportPeriodValueObject(isValid: true, methodResult: MethodResult.Success(), from: start, to: end);
    }

    /// <summary>Início do primeiro dia, em UTC.</summary>
    public DateTime StartsAtUtc => ToUtc(From);

    /// <summary>Início do dia seguinte ao último, em UTC (intervalo semiaberto).</summary>
    public DateTime EndsBeforeUtc => ToUtc(To.AddDays(1));

    public IEnumerable<DateOnly> Days
    {
        get
        {
            for (var day = From; day <= To; day = day.AddDays(1))
                yield return day;
        }
    }

    private static DateTime ToUtc(DateOnly day)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TIME_ZONE);

    private static bool TryParse(string? value, out DateOnly date)
        => DateOnly.TryParseExact(value, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static SalesReportPeriodValueObject Invalid()
        => new(
            isValid: false,
            methodResult: MethodResult.Error([Notification.Error(SALES_PERIOD_INVALID_NOTIFICATION_CODE, SALES_PERIOD_INVALID_NOTIFICATION_MESSAGE)]));
}
