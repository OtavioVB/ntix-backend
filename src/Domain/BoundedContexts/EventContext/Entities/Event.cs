using Domain.BoundedContexts.EventContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.EventContext.Entities;

/// <summary>Campos que o produtor informa na criação e substitui na edição (REQ9).</summary>
public sealed record EventDetails(
    EventTitleValueObject Title,
    EventDescriptionValueObject Description,
    EventCategoryValueObject Category,
    EventPeriodValueObject Period,
    VenueValueObject Venue,
    AgeRatingValueObject AgeRating,
    EventImageUrlValueObject ImageUrl)
{
    public MethodResult GetValidation()
        => MethodResult.Factory(
            Title.MethodResult,
            Description.MethodResult,
            Category.MethodResult,
            Period.MethodResult,
            Venue.MethodResult,
            AgeRating.MethodResult,
            ImageUrl.MethodResult);
}

/// <summary>
/// Evento de um produtor (REQ9). Nasce em <see cref="EventStatus.DRAFT"/>; o slug é gerado na criação e nunca muda (RN4),
/// para não quebrar links já divulgados.
/// </summary>
public sealed record Event
{
    private Event(IdValueObject id, IdValueObject producerId, SlugValueObject slug, EventDetails details, EventStatus status, DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
    {
        Id = id;
        ProducerId = producerId;
        Slug = slug;
        Details = details;
        Status = status;
        CreatedAt = createdAt;
        CreatedCorrelationId = createdCorrelationId;
        UpdatedAt = updatedAt;
        UpdatedCorrelationId = updatedCorrelationId;
    }

    public IdValueObject Id { get; init; }
    public IdValueObject ProducerId { get; init; }
    public SlugValueObject Slug { get; init; }
    public EventDetails Details { get; init; }
    public EventStatus Status { get; init; }
    public DateTimeValueObject CreatedAt { get; init; }
    public IdValueObject CreatedCorrelationId { get; init; }
    public DateTimeValueObject UpdatedAt { get; init; }
    public IdValueObject UpdatedCorrelationId { get; init; }

    /// <summary>Tamanho máximo do slug: o título (120) mais o separador e a versão aleatória de 5 caracteres.</summary>
    public const int SLUG_MAX_LENGTH = EventTitleValueObject.MAX_LENGTH + 1 + SlugValueObject.RANDOM_VERSION_LENGTH;

    private const string EVENT_STARTS_IN_PAST_NOTIFICATION_CODE = "EVENT_PERIOD_INVALID";
    private const string EVENT_STARTS_IN_PAST_NOTIFICATION_MESSAGE = "O evento deve começar no futuro.";

    public const string EVENT_ALREADY_STARTED_NOTIFICATION_CODE = "EVENT_ALREADY_STARTED";
    private const string EVENT_ALREADY_STARTED_NOTIFICATION_MESSAGE = "O evento já começou e não pode mais ser alterado.";

    /// <summary>Slug base do título, antes de conferir se está livre na base.</summary>
    public static SlugValueObject BuildBaseSlug(EventTitleValueObject title)
        => SlugValueObject.Build(title.Value, SLUG_MAX_LENGTH);

    /// <summary>Cria o evento em rascunho. RN5: o início precisa estar no futuro.</summary>
    public static MethodResult<Event> Create(IdValueObject producerId, SlugValueObject slug, EventDetails details, DateTime now, IdValueObject correlationId)
    {
        var validation = details.GetValidation();

        if (validation.IsError)
            return MethodResult<Event>.Error(validation.Notifications);

        if (details.Period.StartsAt <= now)
            return MethodResult<Event>.Error([Notification.Error(EVENT_STARTS_IN_PAST_NOTIFICATION_CODE, EVENT_STARTS_IN_PAST_NOTIFICATION_MESSAGE)]);

        var timestamp = DateTimeValueObject.Build(now);

        return MethodResult<Event>.Success(new Event(
            id: IdValueObject.Build(),
            producerId: producerId,
            slug: slug,
            details: details,
            status: EventStatus.DRAFT,
            createdAt: timestamp,
            createdCorrelationId: correlationId,
            updatedAt: timestamp,
            updatedCorrelationId: correlationId));
    }

    /// <summary>Reconstrói o evento gravado, sem revalidar regras que dependem do relógio.</summary>
    public static Event Restore(IdValueObject id, IdValueObject producerId, SlugValueObject slug, EventDetails details, EventStatus status, DateTimeValueObject createdAt, IdValueObject createdCorrelationId, DateTimeValueObject updatedAt, IdValueObject updatedCorrelationId)
        => new(id, producerId, slug, details, status, createdAt, createdCorrelationId, updatedAt, updatedCorrelationId);

    /// <summary>RN11: evento que já começou não é editado. Reutilizado pelos lotes (REQ10).</summary>
    public bool CanBeEdited(DateTime now)
        => Details.Period.StartsAt > now;

    /// <summary>Substitui os campos editáveis. Slug, situação e criação não mudam.</summary>
    public MethodResult<Event> Update(EventDetails details, DateTime now, IdValueObject correlationId)
    {
        if (!CanBeEdited(now))
            return MethodResult<Event>.Error([Notification.Error(EVENT_ALREADY_STARTED_NOTIFICATION_CODE, EVENT_ALREADY_STARTED_NOTIFICATION_MESSAGE)]);

        var validation = details.GetValidation();

        if (validation.IsError)
            return MethodResult<Event>.Error(validation.Notifications);

        if (details.Period.StartsAt <= now)
            return MethodResult<Event>.Error([Notification.Error(EVENT_STARTS_IN_PAST_NOTIFICATION_CODE, EVENT_STARTS_IN_PAST_NOTIFICATION_MESSAGE)]);

        return MethodResult<Event>.Success(this with
        {
            Details = details,
            UpdatedAt = DateTimeValueObject.Build(now),
            UpdatedCorrelationId = correlationId,
        });
    }

    /// <summary>Nomes dos campos que mudam com os novos detalhes, para o log da edição (sem os valores de texto livre).</summary>
    public IReadOnlyList<string> GetChangedFields(EventDetails details)
    {
        var current = Details;
        var changed = new List<string>();

        if (current.Title.Value != details.Title.Value) changed.Add("title");
        if (current.Description.Value != details.Description.Value) changed.Add("description");
        if (current.Category.Value != details.Category.Value) changed.Add("category");
        if (current.Period.StartsAt != details.Period.StartsAt || current.Period.EndsAt != details.Period.EndsAt || current.Period.DoorsOpenAt != details.Period.DoorsOpenAt) changed.Add("period");
        if (current.Venue.Name != details.Venue.Name || current.Venue.Address != details.Venue.Address || current.Venue.City != details.Venue.City || current.Venue.State != details.Venue.State) changed.Add("venue");
        if (current.AgeRating.Value != details.AgeRating.Value) changed.Add("ageRating");
        if (current.ImageUrl.Value != details.ImageUrl.Value) changed.Add("imageUrl");

        return changed;
    }
}
