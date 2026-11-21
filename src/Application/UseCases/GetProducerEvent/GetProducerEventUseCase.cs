using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetProducerEvent.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetProducerEvent;

/// <summary>Um evento da produtora (REQ9). Evento de outra produtora responde como inexistente (RN1).</summary>
public sealed class GetProducerEventUseCase : IUseCase<GetProducerEventUseCaseInput, ProducerEventOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetProducerEventDiagnostics _diagnostics;
    private readonly ILogger<GetProducerEventUseCase> _logger;

    public GetProducerEventUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ISalesRepository salesRepository,
        TimeProvider timeProvider,
        IUnitOfWork unitOfWork,
        GetProducerEventDiagnostics diagnostics,
        ILogger<GetProducerEventUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _salesRepository = salesRepository;
        _timeProvider = timeProvider;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string GET_PRODUCER_EVENT_CANCELED_NOTIFICATION_CODE = "GET_PRODUCER_EVENT_CANCELED";
    private const string GET_PRODUCER_EVENT_CANCELED_NOTIFICATION_MESSAGE = "A consulta do evento foi cancelada.";

    public async Task<MethodResult<ProducerEventOutput>> ExecuteAsync(GetProducerEventUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetProducerEventDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetProducerEvent", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetProducerEventDiagnostics.OUTCOME_VALIDATION_FAILED);

                GetProducerEventLog.InvalidData(_logger, string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<ProducerEventOutput>.Error(validationResult.Notifications);
            }

            var producerSlug = input.ProducerSlug.Value;

            // Só leitura: a transação dá uma conexão ao repositório e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            var model = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdAsync(producerId.Value, input.EventId.Value, cancellationToken);

            var summary = model is null
                ? EventConversions.NO_SALES
                : (await _salesRepository.GetEventSummariesAsync(model.ProducerId, [model.Id], cancellationToken)).GetValueOrDefault(model.Id, EventConversions.NO_SALES);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (producerId is null)
            {
                RecordOutcome(GetProducerEventDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                GetProducerEventLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<ProducerEventOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            if (model is null)
            {
                RecordOutcome(GetProducerEventDiagnostics.OUTCOME_EVENT_NOT_FOUND);

                GetProducerEventLog.EventNotFound(_logger, producerSlug);

                return MethodResult<ProducerEventOutput>.Error([Notification.Error(EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            RecordOutcome(GetProducerEventDiagnostics.OUTCOME_SERVED);

            GetProducerEventLog.Served(_logger, model.Id, producerSlug);

            return MethodResult<ProducerEventOutput>.Success(EventConversions.ToOutput(model, summary, _timeProvider.GetUtcNow().UtcDateTime));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ProducerEventOutput>.Error([Notification.Error(GET_PRODUCER_EVENT_CANCELED_NOTIFICATION_CODE, GET_PRODUCER_EVENT_CANCELED_NOTIFICATION_MESSAGE)]);

            GetProducerEventLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetProducerEventDiagnostics.OUTCOME_ERROR);

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
            GetProducerEventLog.RollbackFailed(_logger, ex);
        }
    }
}
