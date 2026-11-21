using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.UpdateEvent.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.UpdateEvent;

/// <summary>Substitui os campos editáveis de um evento da produtora (REQ9), até o início dele (RN11). O slug nunca muda (RN4).</summary>
public sealed class UpdateEventUseCase : IUseCase<UpdateEventUseCaseInput, ProducerEventOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateEventDiagnostics _diagnostics;
    private readonly ILogger<UpdateEventUseCase> _logger;

    public UpdateEventUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ISalesRepository salesRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        UpdateEventDiagnostics diagnostics,
        ILogger<UpdateEventUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _salesRepository = salesRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string UPDATE_EVENT_CANCELED_NOTIFICATION_CODE = "UPDATE_EVENT_CANCELED";
    private const string UPDATE_EVENT_CANCELED_NOTIFICATION_MESSAGE = "A alteração do evento foi cancelada.";

    public async Task<MethodResult<ProducerEventOutput>> ExecuteAsync(UpdateEventUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = UpdateEventDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("UpdateEvent", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
                return BuildInvalidDataResult(input, validationResult);

            var producerSlug = input.ProducerSlug.Value;
            // O PostgreSQL guarda microssegundos: o instante é truncado para a resposta ser igual ao que fica gravado.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
                return await RejectAsync(UpdateEventDiagnostics.OUTCOME_PRODUCER_NOT_FOUND, PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE, () => UpdateEventLog.ProducerNotFound(_logger, producerSlug));

            var model = input.EventId is null
                ? null
                : await _eventRepository.GetByIdForUpdateAsync(producerId.Value, input.EventId.Value, cancellationToken);

            if (model is null)
                return await RejectAsync(UpdateEventDiagnostics.OUTCOME_EVENT_NOT_FOUND, EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE, () => UpdateEventLog.EventNotFound(_logger, producerSlug));

            var current = EventConversions.ToEvent(model);
            var updateResult = current.Update(input.Details, now, IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            if (updateResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, updateResult);
            }

            var updated = updateResult.Output!;
            var changedFields = string.Join(", ", current.GetChangedFields(input.Details));

            await _eventRepository.UpdateAsync(EventConversions.ToModel(updated), cancellationToken);

            var summary = (await _salesRepository.GetEventSummariesAsync(updated.ProducerId.Value, [updated.Id.Value], cancellationToken)).GetValueOrDefault(updated.Id.Value, EventConversions.NO_SALES);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(UpdateEventDiagnostics.OUTCOME_UPDATED);

            UpdateEventLog.Updated(_logger, updated.Id.Value, producerSlug, changedFields);

            return MethodResult<ProducerEventOutput>.Success(EventConversions.ToOutput(updated, summary, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ProducerEventOutput>.Error([Notification.Error(UPDATE_EVENT_CANCELED_NOTIFICATION_CODE, UPDATE_EVENT_CANCELED_NOTIFICATION_MESSAGE)]);

            UpdateEventLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(UpdateEventDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private async Task<MethodResult<ProducerEventOutput>> RejectAsync(string outcome, string code, string message, Action log)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(outcome);

        log();

        return MethodResult<ProducerEventOutput>.Error([Notification.Error(code, message)]);
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
            UpdateEventLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ProducerEventOutput> BuildInvalidDataResult(UpdateEventUseCaseInput input, MethodResult validationResult)
    {
        var codes = validationResult.Notifications.Select(notification => notification.Code).ToArray();

        RecordOutcome(codes.Contains(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE)
            ? UpdateEventDiagnostics.OUTCOME_ALREADY_STARTED
            : UpdateEventDiagnostics.OUTCOME_VALIDATION_FAILED);

        UpdateEventLog.InvalidData(
            logger: _logger,
            producerSlug: input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-",
            notificationCodes: string.Join(", ", codes));

        return MethodResult<ProducerEventOutput>.Error(validationResult.Notifications);
    }
}
