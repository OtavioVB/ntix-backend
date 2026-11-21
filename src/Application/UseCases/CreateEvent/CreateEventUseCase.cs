using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateEvent.Inputs;
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

namespace Application.UseCases.CreateEvent;

/// <summary>Cria o evento da produtora em rascunho (REQ9), com slug global gerado do título (RN4).</summary>
public sealed class CreateEventUseCase : IUseCase<CreateEventUseCaseInput, ProducerEventOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly CreateEventDiagnostics _diagnostics;
    private readonly ILogger<CreateEventUseCase> _logger;

    public CreateEventUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CreateEventDiagnostics diagnostics,
        ILogger<CreateEventUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string CREATE_EVENT_CANCELED_NOTIFICATION_CODE = "CREATE_EVENT_CANCELED";
    private const string CREATE_EVENT_CANCELED_NOTIFICATION_MESSAGE = "A criação do evento foi cancelada.";

    public async Task<MethodResult<ProducerEventOutput>> ExecuteAsync(CreateEventUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = CreateEventDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("CreateEvent", auditableInfo);

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
            var correlationId = IdValueObject.Build(auditableInfo.CorrelationId.ToString());

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            if (producerId is null)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                RecordOutcome(CreateEventDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                CreateEventLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<ProducerEventOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var baseSlug = Event.BuildBaseSlug(input.Details.Title);

            if (!baseSlug.IsValid)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, baseSlug.MethodResult);
            }

            var eventResult = Event.Create(
                producerId: IdValueObject.Build(producerId.Value.ToString()),
                slug: await GetAvailableSlugAsync(baseSlug, cancellationToken),
                details: input.Details,
                now: now,
                correlationId: correlationId);

            if (eventResult.IsError)
            {
                await _unitOfWork.RollbackAsync(CancellationToken.None);

                return BuildInvalidDataResult(input, eventResult);
            }

            var @event = eventResult.Output!;

            // A consulta prévia só antecipa: duas criações simultâneas com o mesmo slug caem no índice único e geram outra versão.
            while (await _eventRepository.CreateAsync(EventConversions.ToModel(@event), cancellationToken) == EventCreationResult.SlugAlreadyExists)
            {
                CreateEventLog.SlugTaken(_logger, @event.Slug.Value);

                @event = @event with { Slug = await GetAvailableSlugAsync(@event.Slug.BuildRandomVersion(), cancellationToken) };
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            RecordOutcome(CreateEventDiagnostics.OUTCOME_CREATED);

            Activity.Current?.SetTag("ntix.events.event_id", @event.Id.Value);

            CreateEventLog.Created(_logger, @event.Id.Value, @event.Slug.Value, producerSlug);

            return MethodResult<ProducerEventOutput>.Success(EventConversions.ToOutput(@event, EventConversions.NO_SALES, now));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<ProducerEventOutput>.Error([Notification.Error(CREATE_EVENT_CANCELED_NOTIFICATION_CODE, CREATE_EVENT_CANCELED_NOTIFICATION_MESSAGE)]);

            CreateEventLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(CreateEventDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private async Task<SlugValueObject> GetAvailableSlugAsync(SlugValueObject slug, CancellationToken cancellationToken)
    {
        while (await _eventRepository.ExistsBySlugAsync(slug.Value, cancellationToken))
            slug = slug.BuildRandomVersion();

        return slug;
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
            CreateEventLog.RollbackFailed(_logger, ex);
        }
    }

    private MethodResult<ProducerEventOutput> BuildInvalidDataResult(CreateEventUseCaseInput input, MethodResult validationResult)
    {
        RecordOutcome(CreateEventDiagnostics.OUTCOME_VALIDATION_FAILED);

        CreateEventLog.InvalidData(
            logger: _logger,
            producerSlug: input.ProducerSlug.IsValid ? input.ProducerSlug.Value : "-",
            notificationCodes: string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

        return MethodResult<ProducerEventOutput>.Error(validationResult.Notifications);
    }
}
