using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Events;

public sealed class EventRepository : IEventRepository
{
    private const string CREATE_EVENT_SAVEPOINT_NAME = "create_event";

    private const int TITLE_MAX_LENGTH = 120;
    private const int SLUG_MAX_LENGTH = 126;
    private const int DESCRIPTION_MAX_LENGTH = 5000;
    private const int ENUM_MAX_LENGTH = 32;
    private const int VENUE_NAME_MAX_LENGTH = 120;
    private const int ADDRESS_MAX_LENGTH = 256;
    private const int CITY_MAX_LENGTH = 80;
    private const int STATE_LENGTH = 2;
    private const int IMAGE_URL_MAX_LENGTH = 2048;

    private const string SELECT_COLUMNS = """
        SELECT id_event, id_producer, title, slug, description, category, starts_at, ends_at, doors_open_at,
               venue_name, address, city, state, age_rating, image_url, status,
               created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM events.event
        """;

    private const string EXISTS_BY_SLUG_SQL = "SELECT EXISTS (SELECT 1 FROM events.event WHERE slug = @slug);";

    // O id_producer na consulta é o isolamento entre produtores (RN1): o evento de outro produtor "não existe".
    private const string SELECT_BY_ID_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND id_event = @id_event;
        """;

    private const string SELECT_BY_ID_FOR_UPDATE_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND id_event = @id_event
        FOR UPDATE;
        """;

    private const string SELECT_BY_PRODUCER_SQL = SELECT_COLUMNS + """

        WHERE id_producer = @id_producer AND (@status::varchar IS NULL OR status = @status)
        ORDER BY starts_at, id_event;
        """;

    private const string INSERT_SQL = """
        INSERT INTO events.event (id_event, id_producer, title, slug, description, category, starts_at, ends_at, doors_open_at,
                                  venue_name, address, city, state, age_rating, image_url, status,
                                  created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_event, @id_producer, @title, @slug, @description, @category, @starts_at, @ends_at, @doors_open_at,
                @venue_name, @address, @city, @state, @age_rating, @image_url, @status,
                @created_at, @created_correlation_id, @updated_at, @updated_correlation_id);
        """;

    private const string UPDATE_SQL = """
        UPDATE events.event
        SET title = @title, description = @description, category = @category, starts_at = @starts_at, ends_at = @ends_at,
            doors_open_at = @doors_open_at, venue_name = @venue_name, address = @address, city = @city, state = @state,
            age_rating = @age_rating, image_url = @image_url, updated_at = @updated_at, updated_correlation_id = @updated_correlation_id
        WHERE id_producer = @id_producer AND id_event = @id_event;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public EventRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual, sem valores de parâmetros.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(EventRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "events.event");

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ExistsBySlugAsync));

        await using var command = _unitOfWork.CreateCommand(EXISTS_BY_SLUG_SQL);

        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = slug;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<EventCreationResult> CreateAsync(EventModel @event, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync));

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = @event.Id;
        command.Parameters.Add("@slug", NpgsqlDbType.Varchar, SLUG_MAX_LENGTH).Value = @event.Slug;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = @event.Status;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = @event.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = @event.CreatedCorrelationId;
        AddEditableParameters(command, @event);

        // A violação do slug invalidaria a transação; o savepoint devolve o conflito sem perdê-la.
        await _unitOfWork.SaveAsync(CREATE_EVENT_SAVEPOINT_NAME, cancellationToken);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await _unitOfWork.RollbackToSavepointAsync(CREATE_EVENT_SAVEPOINT_NAME, cancellationToken);

            return EventCreationResult.SlugAlreadyExists;
        }

        await _unitOfWork.ReleaseSavepointAsync(CREATE_EVENT_SAVEPOINT_NAME, cancellationToken);

        return EventCreationResult.Created;
    }

    public Task<EventModel?> GetByIdAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default)
        => GetSingleAsync(SELECT_BY_ID_SQL, nameof(GetByIdAsync), producerId, eventId, cancellationToken);

    public Task<EventModel?> GetByIdForUpdateAsync(Guid producerId, Guid eventId, CancellationToken cancellationToken = default)
        => GetSingleAsync(SELECT_BY_ID_FOR_UPDATE_SQL, nameof(GetByIdForUpdateAsync), producerId, eventId, cancellationToken);

    public async Task<IReadOnlyList<EventModel>> ListByProducerAsync(Guid producerId, string? status, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(ListByProducerAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_PRODUCER_SQL);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = (object?)status ?? DBNull.Value;

        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task UpdateAsync(EventModel @event, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(UpdateAsync));

        await using var command = _unitOfWork.CreateCommand(UPDATE_SQL);

        AddEditableParameters(command, @event);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<EventModel?> GetSingleAsync(string sql, string operation, Guid producerId, Guid eventId, CancellationToken cancellationToken)
    {
        using var activity = StartDatabaseActivity(operation);

        await using var command = _unitOfWork.CreateCommand(sql);

        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = producerId;
        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = eventId;

        return (await ReadEventsAsync(command, cancellationToken)).SingleOrDefault();
    }

    private static void AddEditableParameters(NpgsqlCommand command, EventModel @event)
    {
        command.Parameters.Add("@id_event", NpgsqlDbType.Uuid).Value = @event.Id;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = @event.ProducerId;
        command.Parameters.Add("@title", NpgsqlDbType.Varchar, TITLE_MAX_LENGTH).Value = @event.Title;
        command.Parameters.Add("@description", NpgsqlDbType.Varchar, DESCRIPTION_MAX_LENGTH).Value = (object?)@event.Description ?? DBNull.Value;
        command.Parameters.Add("@category", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = @event.Category;
        command.Parameters.Add("@starts_at", NpgsqlDbType.TimestampTz).Value = @event.StartsAt;
        command.Parameters.Add("@ends_at", NpgsqlDbType.TimestampTz).Value = @event.EndsAt;
        command.Parameters.Add("@doors_open_at", NpgsqlDbType.TimestampTz).Value = (object?)@event.DoorsOpenAt ?? DBNull.Value;
        command.Parameters.Add("@venue_name", NpgsqlDbType.Varchar, VENUE_NAME_MAX_LENGTH).Value = @event.VenueName;
        command.Parameters.Add("@address", NpgsqlDbType.Varchar, ADDRESS_MAX_LENGTH).Value = @event.Address;
        command.Parameters.Add("@city", NpgsqlDbType.Varchar, CITY_MAX_LENGTH).Value = @event.City;
        command.Parameters.Add("@state", NpgsqlDbType.Char, STATE_LENGTH).Value = @event.State;
        command.Parameters.Add("@age_rating", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = @event.AgeRating;
        command.Parameters.Add("@image_url", NpgsqlDbType.Varchar, IMAGE_URL_MAX_LENGTH).Value = (object?)@event.ImageUrl ?? DBNull.Value;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = @event.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = @event.UpdatedCorrelationId;
    }

    private static async Task<IReadOnlyList<EventModel>> ReadEventsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventModel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new EventModel(
                Id: reader.GetGuid(0),
                ProducerId: reader.GetGuid(1),
                Title: reader.GetString(2),
                Slug: reader.GetString(3),
                Description: await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                Category: reader.GetString(5),
                StartsAt: reader.GetDateTime(6),
                EndsAt: reader.GetDateTime(7),
                DoorsOpenAt: await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetDateTime(8),
                VenueName: reader.GetString(9),
                Address: reader.GetString(10),
                City: reader.GetString(11),
                State: reader.GetString(12),
                AgeRating: reader.GetString(13),
                ImageUrl: await reader.IsDBNullAsync(14, cancellationToken) ? null : reader.GetString(14),
                Status: reader.GetString(15),
                CreatedAt: reader.GetDateTime(16),
                CreatedCorrelationId: reader.GetGuid(17),
                UpdatedAt: reader.GetDateTime(18),
                UpdatedCorrelationId: reader.GetGuid(19)));
        }

        return events;
    }
}
