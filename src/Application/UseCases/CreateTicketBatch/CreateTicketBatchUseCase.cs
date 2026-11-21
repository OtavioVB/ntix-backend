using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateTicketBatch.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Enums;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.CreateTicketBatch;

/// <summary>Cria um lote de ingressos num evento da produtora (REQ10), antes do início do evento (RN9).</summary>
public sealed class CreateTicketBatchUseCase : IUseCase<CreateTicketBatchUseCaseInput, TicketBatchOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly CreateTicketBatchDiagnostics _diagnostics;
    private readonly ILogger<CreateTicketBatchUseCase> _logger;

    public CreateTicketBatchUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CreateTicketBatchDiagnostics diagnostics,
        ILogger<CreateTicketBatchUseCase> logger)
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

    public const string BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE = "BATCH_NAME_ALREADY_EXISTS";
    private const string BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE = "Já existe um lote com este nome no evento.";

    public const string CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE = "CREATE_TICKET_BATCH_CANCELED";
    private const string CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_MESSAGE = "A criação do lote foi cancelada.";

    public async Task<MethodResult<TicketBatchOutput>> ExecuteAsync(CreateTicketBatchUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateTicketBatchDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateTicketBatch", auditableInfo);

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

            // A trava do evento serializa a criação de lote com a publicação e a edição do mesmo evento.
            var eventModel = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdForUpdateAsync(producerId.Value, input.EventId.Value, cancellationToken);

            if (eventModel is null)
                return await RejectAsync(CreateTicketBatchDiagnostics.OUTCOME_EVENT_NOT_FOUND, EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE, producerSlug);

            var batchResult = TicketBatch.Create(EventConversions.ToEvent(eventModel), input.Details, now, IdValueObject.Build(auditableInfo.CorrelationId.ToString()));

            if (batchResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, batchResult);
            }

            var batch = batchResult.Output!;

            if (await _ticketBatchRepository.CreateAsync(TicketBatchConversions.ToModel(batch), cancellationToken) == TicketBatchWriteResult.NameAlreadyExists)
                return await RejectAsync(CreateTicketBatchDiagnostics.OUTCOME_NAME_ALREADY_EXISTS, BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE, BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_MESSAGE, producerSlug);

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(CreateTicketBatchDiagnostics.OUTCOME_CREATED);

            CreateTicketBatchLog.Created(_logger, batch.Id.Value, batch.EventId.Value, producerSlug, batch.Details.Price.Cents, batch.Details.Quantity.Value);

            return MethodResult<TicketBatchOutput>.Success(TicketBatchConversions.ToOutput(batch, TicketBatchConversions.NO_SALES, TicketBatchConversions.NO_TOTALS, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<TicketBatchOutput>.Error([Notification.Error(CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE, CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_MESSAGE)]);

            CreateTicketBatchLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateTicketBatchDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private async Task<MethodResult<TicketBatchOutput>> RejectAsync(string outcome, string code, string message, string producerSlug)
    {
        await _unitOfWork.RollbackAsync(CancellationToken.None);

        RecordOutcome(outcome);

        CreateTicketBatchLog.Rejected(_logger, producerSlug, code);

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
            CreateTicketBatchLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<TicketBatchOutput> BuildInvalidDataResult(CreateTicketBatchUseCaseInput input, MethodResult validationResult)
    {
        var codes = validationResult.Notifications.Select(notification => notification.Code).ToArray();

        RecordOutcome(codes.Contains(Event.EVENT_ALREADY_STARTED_NOTIFICATION_CODE)
            ? CreateTicketBatchDiagnostics.OUTCOME_EVENT_ALREADY_STARTED
            : CreateTicketBatchDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateTicketBatchLog.Rejected(_logger, input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-", string.Join(", ", codes));

        return MethodResult<TicketBatchOutput>.Error(validationResult.Notifications);
    }
}
