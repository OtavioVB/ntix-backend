using Application.UseCases.Base;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders;
using Application.UseCases.Base.Payments;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.BoundedContexts.OrderContext.Events;
using Domain.BoundedContexts.OrderContext.Services;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.Cryptography.Credentials.Interfaces;
using Infra.Http.Payments.AbacatePay;
using Infra.Http.Payments.Interfaces;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Enums;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Payments.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Application.UseCases.ConfirmPaymentFromWebhook;

/// <summary>
/// Efetiva o pagamento de um Pix a partir do webhook do AbacatePay (REQ8). A assinatura e o segredo da integração vêm antes de
/// qualquer consulta ao banco. Só a fonte confirma: a situação é consultada com a chave da integração que gerou o Pix (RN4).
/// Confirmado, a transação trava o pedido e grava o pagamento, os ingressos, o evento <c>OrderPaid</c> e o registro do webhook (RN6).
/// </summary>
public sealed class ConfirmPaymentFromWebhookUseCase : IUseCase<ConfirmPaymentFromWebhookUseCaseInput>
{
    public const string ORDER_PAID_EVENT_TYPE = "OrderPaid";

    private const string API_KEY_CREDENTIAL_KEY = "API_KEY";
    private const string WEBHOOK_SECRET_CREDENTIAL_KEY = "WEBHOOK_SECRET";
    private const string PAID_STATUS = "PAID";
    private const int MAX_TICKET_CODE_ATTEMPTS = 3;

    public const string WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE = "WEBHOOK_SIGNATURE_INVALID";
    public const string WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE = "WEBHOOK_SECRET_INVALID";
    public const string WEBHOOK_BODY_INVALID_NOTIFICATION_CODE = "WEBHOOK_BODY_INVALID";
    public const string PAYMENT_NOT_FOUND_NOTIFICATION_CODE = "PAYMENT_NOT_FOUND";
    public const string SOURCE_UNAVAILABLE_NOTIFICATION_CODE = "SOURCE_UNAVAILABLE";
    public const string CONFIRM_PAYMENT_CANCELED_NOTIFICATION_CODE = "CONFIRM_PAYMENT_CANCELED";

    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IProducerIntegrationRepository _integrationRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IPaymentWebhookEventRepository _webhookEventRepository;
    private readonly IReadOnlyList<IPixChargeClient> _pixClients;
    private readonly ICredentialCipher _cipher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly AbacatePayConfiguration _abacatePayConfiguration;
    private readonly ConfirmPaymentFromWebhookDiagnostics _diagnostics;
    private readonly ILogger<ConfirmPaymentFromWebhookUseCase> _logger;

    public ConfirmPaymentFromWebhookUseCase(
        IOrderRepository orderRepository,
        IPaymentRepository paymentRepository,
        IProducerIntegrationRepository integrationRepository,
        ICustomerRepository customerRepository,
        ITicketRepository ticketRepository,
        ISalesRepository salesRepository,
        IOutboxRepository outboxRepository,
        IPaymentWebhookEventRepository webhookEventRepository,
        IEnumerable<IPixChargeClient> pixClients,
        ICredentialCipher cipher,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        AbacatePayConfiguration abacatePayConfiguration,
        ConfirmPaymentFromWebhookDiagnostics diagnostics,
        ILogger<ConfirmPaymentFromWebhookUseCase> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _integrationRepository = integrationRepository;
        _customerRepository = customerRepository;
        _ticketRepository = ticketRepository;
        _salesRepository = salesRepository;
        _outboxRepository = outboxRepository;
        _webhookEventRepository = webhookEventRepository;
        _pixClients = pixClients.ToArray();
        _cipher = cipher;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _abacatePayConfiguration = abacatePayConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<MethodResult> ExecuteAsync(ConfirmPaymentFromWebhookUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = ConfirmPaymentFromWebhookDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("ConfirmPaymentFromWebhook", auditableInfo);

        _diagnostics.Requested();

        try
        {
            // RN1: a assinatura vem antes de ler o JSON e antes de qualquer consulta ao banco.
            if (!WebhookSignature.IsValid(input.RawBody, input.Signature, _abacatePayConfiguration.WebhookPublicKey))
                return Refused(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_INVALID_SIGNATURE, WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE);

            if (!AbacatePayWebhookEnvelope.TryParse(input.RawBody, out var envelope))
                return Refused(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_INVALID_BODY, WEBHOOK_BODY_INVALID_NOTIFICATION_CODE);

            // O PostgreSQL guarda microssegundos: o instante é truncado para a resposta ser igual ao que fica gravado.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            var correlationId = IdValueObject.Build(auditableInfo.CorrelationId.ToString());

            if (!envelope!.IsCompleted)
            {
                await RecordAndCommitAsync(envelope, null, "IGNORED", now, correlationId, cancellationToken);

                ConfirmPaymentFromWebhookLog.Ignored(_logger, envelope.Id, envelope.EventType);

                return Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_IGNORED);
            }

            // Leitura da cobrança e das integrações do produtor. Sem trava: a consulta na fonte não deve segurar o pedido.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var paymentModel = await _paymentRepository.GetByProviderReferenceAsync(envelope.TransparentId, cancellationToken);
            var integrations = paymentModel is null
                ? []
                : await _integrationRepository.GetByProducerAsync(paymentModel.ProducerId, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (paymentModel is null)
            {
                await RecordAndCommitAsync(envelope, null, "PAYMENT_NOT_FOUND", now, correlationId, cancellationToken);

                ConfirmPaymentFromWebhookLog.PaymentNotFound(_logger, envelope.Id);

                return Refused(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_PAYMENT_NOT_FOUND, PAYMENT_NOT_FOUND_NOTIFICATION_CODE);
            }

            var integration = integrations.FirstOrDefault(item => item.Id == paymentModel.IntegrationId);
            var pixClient = _pixClients.FirstOrDefault(item => item.ProviderCode == AbacatePayClient.PROVIDER_CODE);

            if (integration is null || pixClient is null)
                return SourceUnavailable(envelope, "no_integration");

            // RN3: com segredo cadastrado na integração que gerou o Pix, o header precisa ser igual (tempo constante).
            var webhookSecret = FindCredential(integration, WEBHOOK_SECRET_CREDENTIAL_KEY);

            if (webhookSecret is not null)
            {
                var expectedSecret = TryDecrypt(integration.Id, webhookSecret);

                if (expectedSecret is null || !WebhookSignature.SecretMatches(input.Secret, expectedSecret))
                    return Refused(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_INVALID_SECRET, WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE);
            }

            var apiKeyCredential = FindCredential(integration, API_KEY_CREDENTIAL_KEY);
            var apiKey = apiKeyCredential is null ? null : TryDecrypt(integration.Id, apiKeyCredential);

            if (apiKey is null)
                return SourceUnavailable(envelope, "missing_credential");

            // RN4: a fonte é quem confirma. Sem resposta, o webhook volta com 503 e o AbacatePay reenvia.
            var source = await pixClient.GetPixChargeStatusAsync(apiKey, paymentModel.ProviderReference, cancellationToken);

            if (!source.Available)
                return SourceUnavailable(envelope, source.FailureReason!);

            var payment = OrderPaymentConversions.ToPayment(paymentModel);

            if (source.Status != PAID_STATUS)
            {
                await RecordAndCommitAsync(envelope, payment.Id.Value, "NOT_PAID_AT_SOURCE", now, correlationId, cancellationToken);

                ConfirmPaymentFromWebhookLog.NotPaidAtSource(_logger, envelope.Id, payment.Id.Value, source.Status ?? string.Empty);

                return Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_NOT_PAID_AT_SOURCE);
            }

            return await ConfirmAsync(envelope, payment, now, correlationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync();

            _diagnostics.Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_CANCELED);

            return MethodResult.Error([Notification.Error(CONFIRM_PAYMENT_CANCELED_NOTIFICATION_CODE, "A confirmação do pagamento foi cancelada.")]);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            ConfirmPaymentFromWebhookLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            _diagnostics.Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    /// <summary>
    /// Transação da confirmação (RN6). Pedido já pago não muda. Valor divergente não confirma (RN9). A deduplicação do envelope
    /// é o insert do registro: se ele já existe, a transação é desfeita e nada é reprocessado.
    /// </summary>
    private async Task<MethodResult> ConfirmAsync(AbacatePayWebhookEnvelope envelope, Payment payment, DateTime now, IdValueObject correlationId, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        var lockedModel = await _orderRepository.GetByIdForUpdateAsync(payment.OrderId.Value, cancellationToken)
            ?? throw new InvalidOperationException("O pedido da cobrança não existe.");

        var order = OrderConversions.ToOrder(lockedModel);

        // Releitura com o pedido travado: a cobrança pode ter mudado entre a leitura anterior e a trava.
        var currentModel = await _paymentRepository.GetByProviderReferenceAsync(envelope.TransparentId, cancellationToken)
            ?? throw new InvalidOperationException("A cobrança desapareceu durante a confirmação.");

        var current = OrderPaymentConversions.ToPayment(currentModel);

        if (order.Status == OrderStatus.PAID || current.Status == PaymentStatus.PAID)
        {
            await RecordAndCommitInsideAsync(envelope, current.Id.Value, "ALREADY_PAID", now, correlationId, cancellationToken);

            ConfirmPaymentFromWebhookLog.Duplicate(_logger, envelope.Id, current.Id.Value);

            return Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_ALREADY_PAID);
        }

        // RN9: o valor da cobrança é o que o pedido pediu na criação. Divergência não confirma e alerta.
        if (!current.HasAmount(order.TotalCents))
        {
            await RecordAndCommitInsideAsync(envelope, current.Id.Value, "AMOUNT_MISMATCH", now, correlationId, cancellationToken);

            ConfirmPaymentFromWebhookLog.AmountMismatch(_logger, current.Id.Value, order.Code.Value);

            _diagnostics.Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_AMOUNT_MISMATCH);

            return MethodResult.Success();
        }

        var inserted = await _webhookEventRepository.CreateAsync(BuildWebhookEvent(envelope, current.Id.Value, "CONFIRMED", now, correlationId), cancellationToken);

        if (!inserted)
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);

            ConfirmPaymentFromWebhookLog.Duplicate(_logger, envelope.Id, current.Id.Value);

            return Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_DUPLICATE);
        }

        var customer = await _customerRepository.GetByIdAsync(order.CustomerId.Value, cancellationToken)
            ?? throw new InvalidOperationException("O cliente do pedido não existe.");

        var confirmation = order.ConfirmPayment(current, customer.FullName, now, correlationId);

        var ticketCount = await CreateTicketsAsync(confirmation, cancellationToken);

        await _paymentRepository.UpdateStatusAsync(OrderPaymentConversions.ToModel(current.Confirm(now, now, correlationId)), cancellationToken);
        await _orderRepository.UpdateStatusAsync(OrderConversions.ToModel(confirmation.Order), cancellationToken);
        await _outboxRepository.AddAsync(BuildOrderPaidOutboxEvent(confirmation.Event, now, correlationId), cancellationToken);

        // REQ14, RN9: as vendas entram nas estruturas de leitura na mesma transação; só a transição para PAID chega aqui.
        var defaultPartitionRows = await _salesRepository.RegisterPaidOrderAsync(BuildSaleEntries(confirmation.Order, confirmation.Event.PaidAt, correlationId), cancellationToken);

        await _unitOfWork.CommitAsync(cancellationToken);

        ConfirmPaymentFromWebhookLog.Confirmed(_logger, envelope.Id, current.Id.Value, order.Code.Value, ticketCount);

        if (defaultPartitionRows > 0)
        {
            ConfirmPaymentFromWebhookLog.SaleInDefaultPartition(_logger, order.Code.Value);

            _diagnostics.SaleEntriesInDefaultPartition(defaultPartitionRows);
        }

        if (confirmation.PaidAfterExpiration)
        {
            ConfirmPaymentFromWebhookLog.PaidAfterExpiration(_logger, current.Id.Value, order.Code.Value);

            _diagnostics.PaidAfterExpiration();
        }

        _diagnostics.ConfirmationDelay(now - current.CreatedAt.Value);

        return Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_CONFIRMED);
    }

    private static SaleEntryModel[] BuildSaleEntries(Order order, DateTime paidAt, IdValueObject correlationId)
        => order.Items
            .Select(item => new SaleEntryModel(
                OrderItemId: item.Id.Value,
                OrderId: order.Id.Value,
                ProducerId: order.ProducerId.Value,
                EventId: order.EventId.Value,
                TicketBatchId: item.BatchId.Value,
                Quantity: item.Quantity.Value,
                UnitPrice: item.UnitPrice.Amount,
                PaidAt: paidAt,
                CorrelationId: correlationId.Value))
            .ToArray();

    // Código de ingresso repetido (RN7) desfaz só o lote no savepoint; a tentativa seguinte sorteia outros códigos.
    private async Task<int> CreateTicketsAsync(OrderPaymentConfirmation confirmation, CancellationToken cancellationToken)
    {
        var tickets = confirmation.Tickets;

        for (var attempt = 1; attempt <= MAX_TICKET_CODE_ATTEMPTS; attempt++)
        {
            var result = await _ticketRepository.CreateManyAsync(tickets.Select(BuildTicketModel).ToArray(), cancellationToken);

            if (result == TicketCreationResult.Created)
                return tickets.Count;

            tickets = tickets.Select(ticket => ticket.WithNewCode()).ToArray();
        }

        throw new InvalidOperationException("Não foi possível sortear códigos de ingresso únicos.");
    }

    private async Task RecordAndCommitAsync(AbacatePayWebhookEnvelope envelope, Guid? paymentId, string outcome, DateTime now, IdValueObject correlationId, CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        await RecordAndCommitInsideAsync(envelope, paymentId, outcome, now, correlationId, cancellationToken);
    }

    private async Task RecordAndCommitInsideAsync(AbacatePayWebhookEnvelope envelope, Guid? paymentId, string outcome, DateTime now, IdValueObject correlationId, CancellationToken cancellationToken)
    {
        await _webhookEventRepository.CreateAsync(BuildWebhookEvent(envelope, paymentId, outcome, now, correlationId), cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
    }

    private static PaymentWebhookEventModel BuildWebhookEvent(AbacatePayWebhookEnvelope envelope, Guid? paymentId, string outcome, DateTime now, IdValueObject correlationId)
        => new(
            Id: Guid.NewGuid(),
            ProviderCode: AbacatePayClient.PROVIDER_CODE,
            ProviderEventId: envelope.Id,
            EventType: envelope.EventType,
            PaymentId: paymentId,
            Outcome: outcome,
            ReceivedAt: now,
            CreatedCorrelationId: correlationId.Value);

    private static TicketModel BuildTicketModel(Ticket ticket)
        => new(
            Id: ticket.Id.Value,
            OrderItemId: ticket.OrderItemId.Value,
            OrderId: ticket.OrderId.Value,
            CustomerId: ticket.CustomerId.Value,
            EventId: ticket.EventId.Value,
            Code: ticket.Code,
            HolderName: ticket.HolderName,
            Status: ticket.Status.ToString(),
            CreatedAt: ticket.CreatedAt,
            CreatedCorrelationId: ticket.CreatedCorrelationId.Value);

    private static OutboxEventModel BuildOrderPaidOutboxEvent(OrderPaidEvent orderPaid, DateTime now, IdValueObject correlationId)
        => new(
            Id: Guid.NewGuid(),
            EventType: ORDER_PAID_EVENT_TYPE,
            AggregateId: orderPaid.OrderId,
            Payload: JsonSerializer.Serialize(orderPaid),
            CreatedAt: now,
            CreatedCorrelationId: correlationId.Value);

    private static ProducerIntegrationCredentialModel? FindCredential(ProducerIntegrationModel integration, string key)
        => integration.Credentials.FirstOrDefault(item => item.Key == key);

    // Credencial decifrada só no momento da chamada, em memória (REQ7). Falha de decifrar não confirma nada.
    private string? TryDecrypt(Guid integrationId, ProducerIntegrationCredentialModel credential)
    {
        try
        {
            return PaymentConversions.DecryptCredential(_cipher, integrationId, credential);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private MethodResult Refused(string outcome, string code)
    {
        ConfirmPaymentFromWebhookLog.Refused(_logger, code);

        _diagnostics.Finished(outcome);

        return MethodResult.Error([Notification.Error(code, "Webhook recusado.")]);
    }

    private MethodResult SourceUnavailable(AbacatePayWebhookEnvelope envelope, string reason)
    {
        ConfirmPaymentFromWebhookLog.SourceUnavailable(_logger, envelope.Id, reason);

        _diagnostics.Finished(ConfirmPaymentFromWebhookDiagnostics.OUTCOME_SOURCE_UNAVAILABLE);

        return MethodResult.Error([Notification.Error(SOURCE_UNAVAILABLE_NOTIFICATION_CODE, "A consulta na fonte não respondeu.")]);
    }

    private MethodResult Finished(string outcome)
    {
        _diagnostics.Finished(outcome);

        return MethodResult.Success();
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConfirmPaymentFromWebhookLog.RollbackFailed(_logger, ex);
        }
    }
}
