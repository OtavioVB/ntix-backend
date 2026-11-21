using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using NpgsqlTypes;
using System.Diagnostics;

namespace Infra.PostgreSql.Repositories.Payments;

public sealed class PaymentRepository : IPaymentRepository
{
    private const int ENUM_MAX_LENGTH = 32;
    private const int PROVIDER_REFERENCE_MAX_LENGTH = 64;

    private const string INSERT_SQL = """
        INSERT INTO payments.payment (id_payment, id_order, id_producer, id_producer_integration, payment_method, amount, status, provider_reference,
                                      pix_copy_paste, pix_qr_code_image, expires_at, paid_at, created_at, created_correlation_id, updated_at, updated_correlation_id)
        VALUES (@id_payment, @id_order, @id_producer, @id_producer_integration, @payment_method, @amount, @status, @provider_reference,
                @pix_copy_paste, @pix_qr_code_image, @expires_at, @paid_at, @created_at, @created_correlation_id, @updated_at, @updated_correlation_id);
        """;

    private const string SELECT_LATEST_BY_ORDER_SQL = """
        SELECT id_payment, id_order, id_producer, id_producer_integration, payment_method, amount, status, provider_reference,
               pix_copy_paste, pix_qr_code_image, expires_at, paid_at, created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM payments.payment
        WHERE id_order = @id_order
        ORDER BY created_at DESC
        LIMIT 1;
        """;

    private const string SELECT_BY_PROVIDER_REFERENCE_SQL = """
        SELECT id_payment, id_order, id_producer, id_producer_integration, payment_method, amount, status, provider_reference,
               pix_copy_paste, pix_qr_code_image, expires_at, paid_at, created_at, created_correlation_id, updated_at, updated_correlation_id
        FROM payments.payment
        WHERE provider_reference = @provider_reference;
        """;

    private const string UPDATE_STATUS_SQL = """
        UPDATE payments.payment
        SET status = @status, paid_at = @paid_at, updated_at = @updated_at, updated_correlation_id = @updated_correlation_id
        WHERE id_payment = @id_payment;
        """;

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.PostgreSql");

    private readonly PostgreSqlUnitOfWork _unitOfWork;

    public PaymentRepository(PostgreSqlUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // Span manual, sem valores de parâmetros.
    private static Activity? StartDatabaseActivity(string operation)
        => ACTIVITY_SOURCE
            .StartActivity($"{nameof(PaymentRepository)}.{operation}", ActivityKind.Client)
            ?.SetTag("db.system.name", "postgresql")
            .SetTag("db.collection.name", "payments.payment");

    public async Task CreateAsync(PaymentModel payment, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(CreateAsync));

        await using var command = _unitOfWork.CreateCommand(INSERT_SQL);

        command.Parameters.Add("@id_payment", NpgsqlDbType.Uuid).Value = payment.Id;
        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = payment.OrderId;
        command.Parameters.Add("@id_producer", NpgsqlDbType.Uuid).Value = payment.ProducerId;
        command.Parameters.Add("@id_producer_integration", NpgsqlDbType.Uuid).Value = payment.IntegrationId;
        command.Parameters.Add("@payment_method", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = payment.Method;
        command.Parameters.Add("@amount", NpgsqlDbType.Numeric).Value = payment.Amount;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = payment.Status;
        command.Parameters.Add("@provider_reference", NpgsqlDbType.Varchar, PROVIDER_REFERENCE_MAX_LENGTH).Value = payment.ProviderReference;
        command.Parameters.Add("@pix_copy_paste", NpgsqlDbType.Text).Value = payment.PixCopyPaste;
        command.Parameters.Add("@pix_qr_code_image", NpgsqlDbType.Text).Value = payment.PixQrCodeImage;
        command.Parameters.Add("@expires_at", NpgsqlDbType.TimestampTz).Value = payment.ExpiresAt;
        command.Parameters.Add("@paid_at", NpgsqlDbType.TimestampTz).Value = (object?)payment.PaidAt ?? DBNull.Value;
        command.Parameters.Add("@created_at", NpgsqlDbType.TimestampTz).Value = payment.CreatedAt;
        command.Parameters.Add("@created_correlation_id", NpgsqlDbType.Uuid).Value = payment.CreatedCorrelationId;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = payment.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = payment.UpdatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PaymentModel?> GetLatestByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetLatestByOrderAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_LATEST_BY_ORDER_SQL);

        command.Parameters.Add("@id_order", NpgsqlDbType.Uuid).Value = orderId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new PaymentModel(
            Id: reader.GetGuid(0),
            OrderId: reader.GetGuid(1),
            ProducerId: reader.GetGuid(2),
            IntegrationId: reader.GetGuid(3),
            Method: reader.GetString(4),
            Amount: reader.GetDecimal(5),
            Status: reader.GetString(6),
            ProviderReference: reader.GetString(7),
            PixCopyPaste: reader.GetString(8),
            PixQrCodeImage: reader.GetString(9),
            ExpiresAt: reader.GetDateTime(10),
            PaidAt: await reader.IsDBNullAsync(11, cancellationToken) ? null : reader.GetDateTime(11),
            CreatedAt: reader.GetDateTime(12),
            CreatedCorrelationId: reader.GetGuid(13),
            UpdatedAt: reader.GetDateTime(14),
            UpdatedCorrelationId: reader.GetGuid(15));
    }

    public async Task<PaymentModel?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(GetByProviderReferenceAsync));

        await using var command = _unitOfWork.CreateCommand(SELECT_BY_PROVIDER_REFERENCE_SQL);

        command.Parameters.Add("@provider_reference", NpgsqlDbType.Varchar, PROVIDER_REFERENCE_MAX_LENGTH).Value = providerReference;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new PaymentModel(
            Id: reader.GetGuid(0),
            OrderId: reader.GetGuid(1),
            ProducerId: reader.GetGuid(2),
            IntegrationId: reader.GetGuid(3),
            Method: reader.GetString(4),
            Amount: reader.GetDecimal(5),
            Status: reader.GetString(6),
            ProviderReference: reader.GetString(7),
            PixCopyPaste: reader.GetString(8),
            PixQrCodeImage: reader.GetString(9),
            ExpiresAt: reader.GetDateTime(10),
            PaidAt: await reader.IsDBNullAsync(11, cancellationToken) ? null : reader.GetDateTime(11),
            CreatedAt: reader.GetDateTime(12),
            CreatedCorrelationId: reader.GetGuid(13),
            UpdatedAt: reader.GetDateTime(14),
            UpdatedCorrelationId: reader.GetGuid(15));
    }

    public async Task UpdateStatusAsync(PaymentModel payment, CancellationToken cancellationToken = default)
    {
        using var activity = StartDatabaseActivity(nameof(UpdateStatusAsync));

        await using var command = _unitOfWork.CreateCommand(UPDATE_STATUS_SQL);

        command.Parameters.Add("@id_payment", NpgsqlDbType.Uuid).Value = payment.Id;
        command.Parameters.Add("@status", NpgsqlDbType.Varchar, ENUM_MAX_LENGTH).Value = payment.Status;
        command.Parameters.Add("@paid_at", NpgsqlDbType.TimestampTz).Value = (object?)payment.PaidAt ?? DBNull.Value;
        command.Parameters.Add("@updated_at", NpgsqlDbType.TimestampTz).Value = payment.UpdatedAt;
        command.Parameters.Add("@updated_correlation_id", NpgsqlDbType.Uuid).Value = payment.UpdatedCorrelationId;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
