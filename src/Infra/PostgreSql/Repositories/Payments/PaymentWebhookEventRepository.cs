using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Payments;

public sealed class PaymentWebhookEventRepository : IPaymentWebhookEventRepository
{
    private const int PROVIDER_CODE_MAX_LENGTH = 32;
    private const int PROVIDER_EVENT_ID_MAX_LENGTH = 128;
    private const int EVENT_TYPE_MAX_LENGTH = 128;
    private const int OUTCOME_MAX_LENGTH = 32;

    // ON CONFLICT DO NOTHING: a duplicata não erra, então a transação segue viva para a confirmação em andamento.
    private const string INSERT_SQL = """
        INSERT INTO payments.payment_webhook_event (id_payment_webhook_event, provider_code, provider_event_id, event_type, id_payment, outcome,
                                                    received_at, created_correlation_id)
        VALUES (@id_payment_webhook_event, @provider_code, @provider_event_id, @event_type, @id_payment, @outcome, @received_at, @created_correlation_id)
        ON CONFLICT (provider_code, provider_event_id) DO NOTHING;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public PaymentWebhookEventRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> CreateAsync(PaymentWebhookEventModel webhookEvent, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE
            .StartActivity($"{nameof(PaymentWebhookEventRepository)}.{nameof(CreateAsync)}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "payments.payment_webhook_event");

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_payment_webhook_event", NpgsqlDbType.Uuid).Value = webhookEvent.Id;
        command.Parameters.Add("@provider_code", NpgsqlDbType.Varchar, PROVIDER_CODE_MAX_LENGTH).Value = webhookEvent.ProviderCode;
        command.Parameters.Add("@provider_event_id", NpgsqlDbType.Varchar, PROVIDER_EVENT_ID_MAX_LENGTH).Value = webhookEvent.ProviderEventId;
        command.Parameters.Add("@event_type", NpgsqlDbType.Varchar, EVENT_TYPE_MAX_LENGTH).Value = webhookEvent.EventType;
        command.Parameters.Add("@id_payment", NpgsqlDbType.Uuid).Value = (object?)webhookEvent.PaymentId ?? DBNull.Value;
        command.Parameters.Add("@outcome", NpgsqlDbType.Varchar, OUTCOME_MAX_LENGTH).Value = webhookEvent.Outcome;
        command.Parameters.Add("@received_at", NpgsqlDbType.TimestampTz).Value = webhookEvent.ReceivedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = webhookEvent.CreatedCorrelationId;

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
