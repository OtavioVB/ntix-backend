using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetTicketBatches;

/// <summary>Lotes de um evento da produtora (REQ10), com vendidos, reservados, disponíveis e o estado de venda.</summary>
public sealed class GetTicketBatchesUseCase : IUseCase<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly GetTicketBatchesDiagnostics _diagnostics;
    private readonly ILogger<GetTicketBatchesUseCase> _logger;

    public GetTicketBatchesUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        GetTicketBatchesDiagnostics diagnostics,
        ILogger<GetTicketBatchesUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string GET_TICKET_BATCHES_CANCELED_NOTIFICATION_CODE = "GET_TICKET_BATCHES_CANCELED";
    private const string GET_TICKET_BATCHES_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos lotes foi cancelada.";

    public async Task<MethodResult<GetTicketBatchesUseCaseOutput>> ExecuteAsync(GetTicketBatchesUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetTicketBatchesDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetTicketBatches", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetTicketBatchesDiagnostics.OUTCOME_VALIDATION_FAILED);

                GetTicketBatchesLog.Rejected(_logger, "-", string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<GetTicketBatchesUseCaseOutput>.Error(validationResult.Notifications);
            }

            var producerSlug = input.ProducerSlug.Value;
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            // Só leitura: a transação dá uma conexão aos repositórios e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            var eventModel = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdAsync(producerId.Value, input.EventId.Value, cancellationToken);

            if (eventModel is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(GetTicketBatchesDiagnostics.OUTCOME_EVENT_NOT_FOUND);

                GetTicketBatchesLog.Rejected(_logger, producerSlug, EVENT_NOT_FOUND_NOTIFICATION_CODE);

                return MethodResult<GetTicketBatchesUseCaseOutput>.Error([Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var models = await _ticketBatchRepository.ListByEventAsync(producerId!.Value, eventModel.Id, cancellationToken);
            var sales = await _ticketBatchRepository.GetSalesAsync(models.Select(model => model.Id).ToArray(), now, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var batches = models
                .Select(model => TicketBatchConversions.ToOutput(TicketBatchConversions.ToBatch(model), sales.GetValueOrDefault(model.Id, TicketBatchConversions.NO_SALES), now))
                .ToArray();

            RecordOutcome(GetTicketBatchesDiagnostics.OUTCOME_SERVED);

            GetTicketBatchesLog.Served(_logger, eventModel.Id, producerSlug, batches.Length);

            return MethodResult<GetTicketBatchesUseCaseOutput>.Success(new GetTicketBatchesUseCaseOutput(batches));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<GetTicketBatchesUseCaseOutput>.Error([Notification.Error(GET_TICKET_BATCHES_CANCELED_NOTIFICATION_CODE, GET_TICKET_BATCHES_CANCELED_NOTIFICATION_MESSAGE)]);

            GetTicketBatchesLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetTicketBatchesDiagnostics.OUTCOME_ERROR);

            throw;
        }
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
            GetTicketBatchesLog.RollbackFailed(_logger, ex);
        }
    }
}
