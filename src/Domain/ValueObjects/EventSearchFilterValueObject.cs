using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using System.Globalization;

namespace Domain.ValueObjects;

/// <summary>
/// Filtros da vitrine pública (REQ11). Filtro inválido recusa a consulta inteira (RN7), com uma notificação por filtro,
/// todas com o código <c>EVENT_FILTER_INVALID</c>. Paginação: <c>page</c> a partir de 1 e <c>pageSize</c> de 1 a 50 (padrão 20).
/// </summary>
public readonly struct EventSearchFilterValueObject
{
    public bool IsValid { get; }

    public string? ProducerSlug { get; }
    public EventCategory? Category { get; }
    public string? City { get; }
    public string? State { get; }
    public DateTime? StartsFrom { get; }
    public DateTime? StartsTo { get; }
    public int Page { get; }
    public int PageSize { get; }

    public MethodResult MethodResult { get; }

    private EventSearchFilterValueObject(bool isValid, MethodResult methodResult, string? producerSlug = null, EventCategory? category = null, string? city = null, string? state = null, DateTime? startsFrom = null, DateTime? startsTo = null, int page = 1, int pageSize = DEFAULT_PAGE_SIZE)
    {
        IsValid = isValid;
        MethodResult = methodResult;
        ProducerSlug = producerSlug;
        Category = category;
        City = city;
        State = state;
        StartsFrom = startsFrom;
        StartsTo = startsTo;
        Page = page;
        PageSize = pageSize;
    }

    public const int DEFAULT_PAGE_SIZE = 20;
    public const int MAX_PAGE_SIZE = 50;

    public const string EVENT_FILTER_INVALID_NOTIFICATION_CODE = "EVENT_FILTER_INVALID";

    public int Offset => (Page - 1) * PageSize;

    public static EventSearchFilterValueObject Build(string? producer, string? category, string? city, string? state, string? startsFrom, string? startsTo, int? page, int? pageSize)
    {
        var notifications = new List<Notification>();

        void Reject(string message) => notifications.Add(Notification.Error(EVENT_FILTER_INVALID_NOTIFICATION_CODE, message));

        string? producerSlug = null;
        if (!string.IsNullOrEmpty(producer))
        {
            var slug = SlugValueObject.Parse(producer);
            if (slug.IsValid) producerSlug = slug.Value;
            else Reject("O filtro de produtora deve ser o slug da produtora.");
        }

        EventCategory? parsedCategory = null;
        if (!string.IsNullOrEmpty(category))
        {
            var categoryValue = EventCategoryValueObject.Build(category);
            if (categoryValue.IsValid) parsedCategory = categoryValue.Value;
            else Reject("A categoria do filtro não existe.");
        }

        var trimmedCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (trimmedCity is { Length: > VenueValueObject.CITY_MAX_LENGTH })
            Reject("A cidade do filtro deve ter até 80 caracteres.");

        string? parsedState = null;
        if (!string.IsNullOrEmpty(state))
        {
            var stateValue = BrazilianStateValueObject.Build(state);
            if (stateValue.IsValid) parsedState = stateValue.Value;
            else Reject("A UF do filtro não existe.");
        }

        var from = ParseDate(startsFrom, "A data inicial do filtro deve estar no formato ISO 8601.", Reject);
        var to = ParseDate(startsTo, "A data final do filtro deve estar no formato ISO 8601.", Reject);

        if (from is not null && to is not null && to < from)
            Reject("A data final do filtro deve ser depois da inicial.");

        if (page is < 1)
            Reject("A página deve ser a partir de 1.");

        if (pageSize is < 1 or > MAX_PAGE_SIZE)
            Reject("O tamanho da página deve ser de 1 a 50.");

        if (notifications.Count > 0)
            return new EventSearchFilterValueObject(isValid: false, methodResult: MethodResult.Error([.. notifications]));

        return new EventSearchFilterValueObject(
            isValid: true,
            methodResult: MethodResult.Success(),
            producerSlug: producerSlug,
            category: parsedCategory,
            city: trimmedCity,
            state: parsedState,
            startsFrom: from,
            startsTo: to,
            page: page ?? 1,
            pageSize: pageSize ?? DEFAULT_PAGE_SIZE);
    }

    private static DateTime? ParseDate(string? value, string message, Action<string> reject)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        reject(message);

        return null;
    }
}
