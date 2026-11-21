using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.UpdateTicketBatch.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.UpdateTicketBatch;

/// <summary>Substitui os campos de um lote (REQ10). Pausar é <c>INACTIVE</c>; a quantidade não fica abaixo do vendido (RN6).</summary>
public sealed class UpdateTicketBatchUseCase : IUseCase<UpdateTicketBatchUseCaseInput, TicketBatchOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly UpdateTicketBatchDiagnostics _diagnostics;
    private readonly ILogger<UpdateTicketBatchUseCase> _logger;

    public UpdateTicketBatchUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        ISalesRepository salesRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        UpdateTicketBatchDiagnostics diagnostics,
        ILogger<UpdateTicketBatchUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _salesRepository = salesRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string EVENT_NOT_FOUND_NOTIFICATION_CODE = "EVENT_NOT_FOUND";
    private const string EVENT_NOT_FOUND_NOTIFICATION_MESSAGE = "O evento informado não existe.";

    public const string BATCH_NOT_FOUND_NOTIFICATION_CODE = "BATCH_NOT_FOUND";
    private const string BATCH_NOT_FOUND_NOTIFICATION_MESSAGE = "O lote informado não existe.";

    public const string BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE = "BATCH_NAME_ALREADY_EXISTS";
    private const string BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe um lote com este nome no evento.";

    public const string UPDATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE = "UPDATE_TICKET_BATCH_CANCELED";
    private const string UPDATE_TICKET_BATCH_CANCELED_NOTIFICATION_MESSAGE = "A alteração do lote foi cancelada.";

    public async Task<MethodResult<TicketBatchOutput>> ExecuteAsync(UpdateTicketBatchUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = UpdateTicketBatchDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("UpdateTicketBatch", auditableInfo);

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

            var eventModel = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdAsync(producerId.Value, input.EventId.Value, cancellationToken);

            if (eventModel is null)
                return await RejectAsync(UpdateTicketBatchDiagnostics.OUTCOME_EVENT_NOT_FOUND, EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE, producerSlug);

            // A trava do lote é a mesma que a reserva do pedido (REQ6) usa: quantidade e vendidos não mudam no meio da edição.
            var batchModel = input.BatchId is null
                ? null
                : await _ticketBatchRepository.GetByIdForUpdateAsync(producerId!.Value, eventModel.Id, input.BatchId.Value, cancellationToken);

            if (batchModel is null)
                return await RejectAsync(UpdateTicketBatchDiagnostics.OUTCOME_BATCH_NOT_FOUND, BATCH_NOT_FOUND_NOTIFICATION_CODE, BATCH_NOT_FOUND_NOTIFICATION_MESSAGE, producerSlug);

            var sales = (await _ticketBatchRepository.GetSalesAsync([batchModel.Id], now, cancellationToken)).GetValueOrDefault(batchModel.Id, TicketBatchConversions.NO_SALES);

            var updateResult = TicketBatchConversions.ToBatch(batchModel).Update(
                @event: EventConversions.ToEvent(eventModel),
                details: input.Details,
                soldAndReserved: sales.Sold + sales.Reserved,
                now: now,
                correlationId: IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            if (updateResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, updateResult);
            }

            var batch = updateResult.Output!;

            if (await _ticketBatchRepository.UpdateAsync(TicketBatchConversions.ToModel(batch), cancellationToken) == TicketBatchWriteResult.NameAlreadyExists)
                return await RejectAsync(UpdateTicketBatchDiagnostics.OUTCOME_NAME_ALREADY_EXISTS, BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE, producerSlug);

            var totals = (await _salesRepository.GetBatchTotalsAsync([batch.Id.Value], cancellationToken)).GetValueOrDefault(batch.Id.Value, TicketBatchConversions.NO_TOTALS);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(UpdateTicketBatchDiagnostics.OUTCOME_UPDATED);

            UpdateTicketBatchLog.Updated(_logger, batch.Id.Value, batch.EventId.Value, producerSlug, batch.Details.Status.Value.ToString(), batch.Details.Price.Cents, batch.Details.Quantity.Value);

            return MethodResult<TicketBatchOutput>.Success(TicketBatchConversions.ToOutput(batch, sales, totals, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<TicketBatchOutput>.Error([Notification.Error(UPDATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE, UPDATE_TICKET_BATCH_CANCELED_NOTIFICATION_MESSAGE)]);

            UpdateTicketBatchLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(UpdateTicketBatchDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private async Task<MethodResult<TicketBatchOutput>> RejectAsync(string outcome, string code, string message, string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(outcome);

        UpdateTicketBatchLog.Rejected(_logger, producerSlug, code);

        return MethodResult<TicketBatchOutput>.Error([Notification.Error(code, message)]);
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
            UpdateTicketBatchLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<TicketBatchOutput> BuildInvalidDataResult(UpdateTicketBatchUseCaseInput input, MethodResult validationResult)
    {
        var codes = validationResult.Notifications.Select(notification => notification.Code).ToArray();

        var outcome = codes.Contains(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE) ? UpdateTicketBatchDiagnostics.OUTCOME_EVENT_ALREADY_STARTED
            : codes.Contains(TicketBatch.BATCH_QUANTITY_BELOW_SOLD_NOTIFICATION_CODE) ? UpdateTicketBatchDiagnostics.OUTCOME_QUANTITY_BELOW_SOLD
            : UpdateTicketBatchDiagnostics.OUTCOME_VALIDATION_FAILED;

        RecordOutcome(outcome);

        UpdateTicketBatchLog.Rejected(_logger, input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-", string.Join(", ", codes));

        return MethodResult<TicketBatchOutput>.Error(validationResult.Notifications);
    }
}
