using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetProducerEvents.Inputs;
using Application.UseCases.GetProducerEvents.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetProducerEvents;

/// <summary>Eventos da produtora para o painel (REQ9), por início crescente, com filtro opcional de situação.</summary>
public sealed class GetProducerEventsUseCase : IUseCase<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetProducerEventsDiagnostics _diagnostics;
    private readonly ILogger<GetProducerEventsUseCase> _logger;

    public GetProducerEventsUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        IUnitOfWork unitOfWork,
        GetProducerEventsDiagnostics diagnostics,
        ILogger<GetProducerEventsUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string PRODUCER_NOT_FOUND_NOTIFICATION_CODE = "PRODUCER_NOT_FOUND";
    private const string PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE = "A produtora informada não existe.";

    public const string GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_CODE = "GET_PRODUCER_EVENTS_CANCELED";
    private const string GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos eventos foi cancelada.";

    public async Task<MethodResult<GetProducerEventsUseCaseOutput>> ExecuteAsync(GetProducerEventsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetProducerEventsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetProducerEvents", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetProducerEventsDiagnostics.OUTCOME_VALIDATION_FAILED);

                GetProducerEventsLog.InvalidData(_logger, string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<GetProducerEventsUseCaseOutput>.Error(validationResult.Notifications);
            }

            var producerSlug = input.ProducerSlug.Value;
            var status = input.Status?.Value.ToString();

            // Só leitura: a transação dá uma conexão ao repositório e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            var models = producerId is null
                ? []
                : await _eventRepository.ListByProducerAsync(producerId.Value, status, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (producerId is null)
            {
                RecordOutcome(GetProducerEventsDiagnostics.OUTCOME_PRODUCER_NOT_FOUND);

                GetProducerEventsLog.ProducerNotFound(_logger, producerSlug);

                return MethodResult<GetProducerEventsUseCaseOutput>.Error([Notification.Error(PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            RecordOutcome(GetProducerEventsDiagnostics.OUTCOME_SERVED);

            GetProducerEventsLog.Served(_logger, producerSlug, status ?? "ALL", models.Count);

            return MethodResult<GetProducerEventsUseCaseOutput>.Success(new GetProducerEventsUseCaseOutput(models.Select(EventConversions.ToOutput).ToArray()));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<GetProducerEventsUseCaseOutput>.Error([Notification.Error(GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_CODE, GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_MESSAGE)]);

            GetProducerEventsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetProducerEventsDiagnostics.OUTCOME_ERROR);

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
            GetProducerEventsLog.RollbackFailed(_logger, ex);
        }
    }
}
