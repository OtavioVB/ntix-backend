using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments;
using Application.UseCases.PublishEvent.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.BoundedContexts.EventContext.Events;
using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Services;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Payments.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Application.UseCases.PublishEvent;

/// <summary>
/// Publica o evento da produtora (REQ10, RN10): rascunho, antes do início, com lote em venda futura e com algum método de
/// pagamento que a Ntix já cobra disponível no ranqueamento do REQ4. Grava <c>EventPublished</c> na outbox (RN11).
/// </summary>
public sealed class PublishEventUseCase : IUseCase<PublishEventUseCaseInput, ProducerEventOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IPaymentProviderRepository _paymentProviderRepository;
    private readonly IProducerIntegrationRepository _producerIntegrationRepository;
    private readonly IPaymentRouteRepository _paymentRouteRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly PublishEventDiagnostics _diagnostics;
    private readonly ILogger<PublishEventUseCase> _logger;

    public PublishEventUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IPaymentProviderRepository paymentProviderRepository,
        IProducerIntegrationRepository producerIntegrationRepository,
        IPaymentRouteRepository paymentRouteRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        PublishEventDiagnostics diagnostics,
        ILogger<PublishEventUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _paymentProviderRepository = paymentProviderRepository;
        _producerIntegrationRepository = producerIntegrationRepository;
        _paymentRouteRepository = paymentRouteRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string PUBLISH_EVENT_CANCELED_NOTIFICATION_CODE = "PUBLISH_EVENT_CANCELED";
    private const string PUBLISH_EVENT_CANCELED_NOTIFICATION_MESSAGE = "A publicação do evento foi cancelada.";

    public async Task<MethodResult<ProducerEventOutput>> ExecuteAsync(PublishEventUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = PublishEventDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("PublishEvent", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(PublishEventDiagnostics.OUTCOME_VALIDATION_FAILED);

                PublishEventLog.Rejected(_logger, "-", string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<ProducerEventOutput>.Error(validationResult.Notifications);
            }

            var producerSlug = input.ProducerSlug.Value;

            // O PostgreSQL guarda microssegundos: o instante é truncado para a resposta ser igual ao que fica gravado.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            // A trava do evento impede duas publicações simultâneas e um lote criado no meio da publicação.
            var eventModel = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdForUpdateAsync(producerId.Value, input.EventId.Value, cancellationToken);

            if (eventModel is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(PublishEventDiagnostics.OUTCOME_EVENT_NOT_FOUND);

                PublishEventLog.Rejected(_logger, producerSlug, EVENT_NOT_FOUND_NOTIFICATION_CODE);

                return MethodResult<ProducerEventOutput>.Error([Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var batches = (await _ticketBatchRepository.ListByEventAsync(producerId!.Value, eventModel.Id, cancellationToken))
                .Select(TicketBatchConversions.ToBatch)
                .ToArray();

            var publishResult = EventConversions.ToEvent(eventModel).Publish(
                batches: batches,
                hasPaymentMethod: await HasPaymentMethodAsync(producerId.Value, cancellationToken),
                now: now,
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            if (publishResult.IsError)
                return await RejectAsync(producerSlug, publishResult.Notifications);

            var published = publishResult.Output!;

            await _eventRepository.UpdateStatusAsync(EventConversions.ToModel(published), cancellationToken);
            await _outboxRepository.AddAsync(BuildEventPublishedOutboxEvent(published), cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(PublishEventDiagnostics.OUTCOME_PUBLISHED);

            PublishEventLog.Published(_logger, published.Id.Value, published.Slug.Value, producerSlug, batches.Length);

            return MethodResult<ProducerEventOutput>.Success(EventConversions.ToOutput(published));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ProducerEventOutput>.Error([Notification.Error(PUBLISH_EVENT_CANCELED_NOTIFICATION_CODE, PUBLISH_EVENT_CANCELED_NOTIFICATION_MESSAGE)]);

            PublishEventLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(PublishEventDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    // O mesmo ranqueamento do REQ4 (serviço de domínio), sobre as rotas lidas nesta transação: basta um método com adaptador
    // na Ntix ter alguma integração capaz de processá-lo agora.
    private async Task<bool> HasPaymentMethodAsync(Guid producerId, CancellationToken cancellationToken)
    {
        var routes = await _paymentRouteRepository.GetByProducerAsync(producerId, cancellationToken);

        if (routes.Count == 0)
            return false;

        var providersById = PaymentConversions.ToProvidersById(await _paymentProviderRepository.GetAllAsync(cancellationToken));
        var integrationsById = (await _producerIntegrationRepository.GetByProducerAsync(producerId, cancellationToken))
            .Select(model => PaymentConversions.ToIntegration(model, providersById))
            .ToDictionary(integration => integration.Id.Value);

        return PaymentMethodsWithAdapter.ALL.Any(method =>
        {
            var route = PaymentConversions.ToRoute(producerId, method, routes.Where(model => model.PaymentMethod == method.ToString()), integrationsById);

            return PaymentIntegrationRanking.Rank(route).IsSuccess;
        });
    }

    private static OutboxEventModel BuildEventPublishedOutboxEvent(Event published)
    {
        var eventPublished = new EventPublishedEvent(published.Id.Value, published.ProducerId.Value, published.Slug.Value, published.UpdatedAt.Value);

        return new OutboxEventModel(
            Id: Guid.NewGuid(),
            EventType: EventPublishedEvent.EVENT_TYPE,
            AggregateId: published.Id.Value,
            Payload: JsonSerializer.Serialize(eventPublished),
            CreatedAt: published.UpdatedAt.Value,
            CreatedCorrelationId: published.UpdatedCorrelationId.Value);
    }

    private async Task<MethodResult<ProducerEventOutput>> RejectAsync(string producerSlug, Domain.CrossCutting.NotificationContext.Interfaces.INotification[] notifications)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        var code = notifications[0].Code;

        RecordOutcome(code switch
        {
            Event.EVENT_NOT_DRAFT_NOTIFICATION_CODE => PublishEventDiagnostics.OUTCOME_NOT_DRAFT,
            Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE => PublishEventDiagnostics.OUTCOME_EVENT_ALREADY_STARTED,
            Event.EVENT_WITHOUT_BATCH_NOTIFICATION_CODE => PublishEventDiagnostics.OUTCOME_WITHOUT_BATCH,
            _ => PublishEventDiagnostics.OUTCOME_WITHOUT_PAYMENT_METHOD,
        });

        PublishEventLog.Rejected(_logger, producerSlug, code);

        return MethodResult<ProducerEventOutput>.Error(notifications);
    }

    private void RecordOutcome(string outcome)
    {
        _diagnostics.Finished(outcome);

        Activity.Current?.SetTag("ntix.outcome", outcome);
    }

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            PublishEventLog.RollbackFailed(_logger, ex);
        }
    }
}
