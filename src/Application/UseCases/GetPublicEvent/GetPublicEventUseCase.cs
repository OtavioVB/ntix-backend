using Application.UseCases.Base;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetPublicEvent.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetPublicEvent;

/// <summary>
/// Página pública do evento (REQ11): só evento publicado (RN1), inclusive encerrado, com os lotes visíveis, o estado de venda
/// e o <c>maxSelectable</c> de cada lote (RN5).
/// </summary>
public sealed class GetPublicEventUseCase : IUseCase<GetPublicEventUseCaseInput, PublicEventOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly OrderConfiguration _orderConfiguration;
    private readonly GetPublicEventDiagnostics _diagnostics;
    private readonly ILogger<GetPublicEventUseCase> _logger;

    public GetPublicEventUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        OrderConfiguration orderConfiguration,
        GetPublicEventDiagnostics diagnostics,
        ILogger<GetPublicEventUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _orderConfiguration = orderConfiguration;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    private const int EVENT_SLUG_MAX_LENGTH = 126;

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_CODE = "GET_PUBLIC_EVENT_CANCELED";
    private const string GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_MESSAGE = "A consulta do evento foi cancelada.";

    public async Task<MethodResult<PublicEventOutput>> ExecuteAsync(GetPublicEventUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetPublicEventDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetPublicEvent", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetPublicEventDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<PublicEventOutput>.Error(validationResult.Notifications);
            }

            if (input.EventSlug.Length == 0 || input.EventSlug.Length > EVENT_SLUG_MAX_LENGTH)
                return NotFound(input.EventSlug);

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            // Só leitura: a transação dá uma conexão aos repositórios e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var @event = await _eventRepository.GetPublishedBySlugAsync(input.EventSlug, cancellationToken);

            if (@event is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return NotFound(input.EventSlug);
            }

            var batches = await _ticketBatchRepository.ListByEventIdsAsync([@event.Id], cancellationToken);
            var sales = await _ticketBatchRepository.GetSalesAsync(batches.Select(batch => batch.Id).ToArray(), now, cancellationToken);
            var producers = (await _producerRepository.GetProfilesByIdsAsync([@event.ProducerId], cancellationToken))
                .ToDictionary(profile => profile.Id, profile => new PublicProducerOutput(profile.Slug, profile.Name));

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var output = PublicEventConversions.ToDetail(
                @event,
                PublicEventConversions.ToProducer(@event.ProducerId, producers),
                PublicEventConversions.Evaluate(batches, sales, now),
                _orderConfiguration.MaxTicketsPerOrder);

            RecordOutcome(GetPublicEventDiagnostics.OUTCOME_SERVED);

            GetPublicEventLog.Served(_logger, @event.Slug, output.SaleState, output.Batches.Count);

            return MethodResult<PublicEventOutput>.Success(output);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<PublicEventOutput>.Error([Notification.Error(GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_CODE, GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_MESSAGE)]);

            GetPublicEventLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetPublicEventDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private MethodResult<PublicEventOutput> NotFound(string eventSlug)
    {
        RecordOutcome(GetPublicEventDiagnostics.OUTCOME_EVENT_NOT_FOUND);

        // O slug do evento é o título público, não um dado pessoal; vai truncado para o log não crescer com entradas longas.
        GetPublicEventLog.NotFound(_logger, eventSlug.Length > EVENT_SLUG_MAX_LENGTH ? eventSlug[..EVENT_SLUG_MAX_LENGTH] : eventSlug);

        return MethodResult<PublicEventOutput>.Error([Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);
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
            GetPublicEventLog.RollbackFailed(_logger, ex);
        }
    }
}
