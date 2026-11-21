using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetEventDailySales.Inputs;
using Application.UseCases.GetEventDailySales.Outputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace Application.UseCases.GetEventDailySales;

/// <summary>
/// Vendas por dia de um evento da produtora (REQ14, RN11), lidas de <c>orders.sale_entry</c> com poda de partições.
/// Os dias sem venda saem zerados, para o gráfico não ter buracos.
/// </summary>
public sealed class GetEventDailySalesUseCase : IUseCase<GetEventDailySalesUseCaseInput, GetEventDailySalesUseCaseOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ISalesRepository _salesRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly GetEventDailySalesDiagnostics _diagnostics;
    private readonly ILogger<GetEventDailySalesUseCase> _logger;

    public GetEventDailySalesUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ISalesRepository salesRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        GetEventDailySalesDiagnostics diagnostics,
        ILogger<GetEventDailySalesUseCase> logger)
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

    public const string GET_EVENT_DAILY_SALES_CANCELED_NOTIFICATION_CODE = "GET_EVENT_DAILY_SALES_CANCELED";
    private const string GET_EVENT_DAILY_SALES_CANCELED_NOTIFICATION_MESSAGE = "A consulta das vendas por dia foi cancelada.";

    public async Task<MethodResult<GetEventDailySalesUseCaseOutput>> ExecuteAsync(GetEventDailySalesUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetEventDailySalesDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetEventDailySales", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var period = input.BuildPeriod(_timeProvider.GetUtcNow().UtcDateTime);

            var validationResult = MethodResult.Factory(input.GetInputValidation(period), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(GetEventDailySalesDiagnostics.OUTCOME_VALIDATION_FAILED);

                GetEventDailySalesLog.Rejected(_logger, "-", string.Join(", ", validationResult.Notifications.Select(notification => notification.Code)));

                return MethodResult<GetEventDailySalesUseCaseOutput>.Error(validationResult.Notifications);
            }

            var producerSlug = input.ProducerSlug.Value;

            // Só leitura: a transação dá uma conexão aos repositórios e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var producerId = await _producerRepository.GetIdBySlugAsync(producerSlug, cancellationToken);

            var eventModel = producerId is null || input.EventId is null
                ? null
                : await _eventRepository.GetByIdAsync(producerId.Value, input.EventId.Value, cancellationToken);

            var sales = eventModel is null
                ? []
                : await _salesRepository.GetDailySalesAsync(eventModel.Id, period.StartsAtUtc, period.EndsBeforeUtc, SalesReportPeriodValueObject.TIME_ZONE.Id, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (producerId is null)
                return Reject(GetEventDailySalesDiagnostics.OUTCOME_EVENT_NOT_FOUND, PRODUCER_NOT_FOUND_NOTIFICATION_CODE, PRODUCER_NOT_FOUND_NOTIFICATION_MESSAGE, producerSlug);

            if (eventModel is null)
                return Reject(GetEventDailySalesDiagnostics.OUTCOME_EVENT_NOT_FOUND, EVENT_NOT_FOUND_NOTIFICATION_CODE, EVENT_NOT_FOUND_NOTIFICATION_MESSAGE, producerSlug);

            var byDay = sales.ToDictionary(day => day.Date);

            var days = period.Days
                .Select(day => byDay.TryGetValue(day, out var found)
                    ? new DailySalesOutput(Format(day), found.SoldQuantity, EventConversions.ToCents(found.GrossAmount), found.PaidOrders)
                    : new DailySalesOutput(Format(day), 0, 0, 0))
                .ToArray();

            RecordOutcome(GetEventDailySalesDiagnostics.OUTCOME_SERVED);

            GetEventDailySalesLog.Served(_logger, eventModel.Id, producerSlug, days.Length, sales.Count);

            return MethodResult<GetEventDailySalesUseCaseOutput>.Success(new GetEventDailySalesUseCaseOutput(Format(period.From), Format(period.To), days));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<GetEventDailySalesUseCaseOutput>.Error([Notification.Error(GET_EVENT_DAILY_SALES_CANCELED_NOTIFICATION_CODE, GET_EVENT_DAILY_SALES_CANCELED_NOTIFICATION_MESSAGE)]);

            GetEventDailySalesLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(GetEventDailySalesDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private static string Format(DateOnly day)
        => day.ToString(SalesReportPeriodValueObject.DATE_FORMAT, CultureInfo.InvariantCulture);

    private MethodResult<GetEventDailySalesUseCaseOutput> Reject(string outcome, string code, string message, string producerSlug)
    {
        RecordOutcome(outcome);

        GetEventDailySalesLog.Rejected(_logger, producerSlug, code);

        return MethodResult<GetEventDailySalesUseCaseOutput>.Error([Notification.Error(code, message)]);
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
            GetEventDailySalesLog.RollbackFailed(_logger, ex);
        }
    }
}
