using Application.UseCases.Base;
using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.SearchPublicEvents.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Events.Interfaces;
using Infra.PostgreSql.Repositories.Events.Models;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.SearchPublicEvents;

/// <summary>
/// Lista da vitrine (REQ11): eventos publicados que não terminaram, de todas as produtoras (hub) ou de uma (loja), com o
/// estado de venda e o "a partir de" calculados pelos lotes. Sem estoque nem vendidos na resposta (RN5).
/// </summary>
public sealed class SearchPublicEventsUseCase : IUseCase<SearchPublicEventsUseCaseInput, PublicEventsPageOutput>
{
    private readonly IProducerRepository _producerRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITicketBatchRepository _ticketBatchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly SearchPublicEventsDiagnostics _diagnostics;
    private readonly ILogger<SearchPublicEventsUseCase> _logger;

    public SearchPublicEventsUseCase(
        IProducerRepository producerRepository,
        IEventRepository eventRepository,
        ITicketBatchRepository ticketBatchRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        SearchPublicEventsDiagnostics diagnostics,
        ILogger<SearchPublicEventsUseCase> logger)
    {
        _producerRepository = producerRepository;
        _eventRepository = eventRepository;
        _ticketBatchRepository = ticketBatchRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public const string SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_CODE = "SEARCH_PUBLIC_EVENTS_CANCELED";
    private const string SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos eventos foi cancelada.";

    public async Task<MethodResult<PublicEventsPageOutput>> ExecuteAsync(SearchPublicEventsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = SearchPublicEventsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("SearchPublicEvents", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                RecordOutcome(SearchPublicEventsDiagnostics.OUTCOME_VALIDATION_FAILED);

                SearchPublicEventsLog.InvalidFilter(_logger, string.Join(", ", validationResult.Notifications.Select(notification => notification.Message)));

                return MethodResult<PublicEventsPageOutput>.Error(validationResult.Notifications);
            }

            var filter = input.Filter;
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            // Só leitura: a transação dá uma conexão aos repositórios e é desfeita no fim.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            Guid? producerId = null;

            if (filter.ProducerSlug is not null)
            {
                producerId = await _producerRepository.GetIdBySlugAsync(filter.ProducerSlug, cancellationToken);

                // Loja de produtora que não existe: lista vazia, sem revelar se o slug existe.
                if (producerId is null)
                {
                    await _unitOfWork.RollbackAsync(CancellationToken.None);

                    return Served(filter, new PublicEventsPageOutput([], filter.Page, filter.PageSize, 0));
                }
            }

            var search = await _eventRepository.SearchPublishedAsync(
                new EventSearchModel(producerId, filter.Category?.ToString(), filter.City, filter.State, filter.StartsFrom, filter.StartsTo, filter.Offset, filter.PageSize),
                now,
                cancellationToken);

            var eventIds = search.Items.Select(@event => @event.Id).ToArray();
            var batches = await _ticketBatchRepository.ListByEventIdsAsync(eventIds, cancellationToken);
            var sales = await _ticketBatchRepository.GetSalesAsync(batches.Select(batch => batch.Id).ToArray(), now, cancellationToken);
            var producers = (await _producerRepository.GetProfilesByIdsAsync(search.Items.Select(@event => @event.ProducerId).ToArray(), cancellationToken))
                .ToDictionary(profile => profile.Id, profile => new PublicProducerOutput(profile.Slug, profile.Name));

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var batchesByEvent = batches.ToLookup(batch => batch.EventId);

            var items = search.Items
                .Select(@event => PublicEventConversions.ToSummary(
                    @event,
                    PublicEventConversions.ToProducer(@event.ProducerId, producers),
                    PublicEventConversions.Summarize(PublicEventConversions.Evaluate(batchesByEvent[@event.Id], sales, now))))
                .ToArray();

            return Served(filter, new PublicEventsPageOutput(items, filter.Page, filter.PageSize, search.TotalItems));
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                return MethodResult<PublicEventsPageOutput>.Error([Notification.Error(SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_CODE, SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_MESSAGE)]);

            SearchPublicEventsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            RecordOutcome(SearchPublicEventsDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private MethodResult<PublicEventsPageOutput> Served(EventSearchFilterValueObject filter, PublicEventsPageOutput page)
    {
        RecordOutcome(SearchPublicEventsDiagnostics.OUTCOME_SERVED);

        SearchPublicEventsLog.Served(
            logger: _logger,
            producerSlug: filter.ProducerSlug ?? "-",
            category: filter.Category?.ToString() ?? "-",
            state: filter.State ?? "-",
            page: page.Page,
            count: page.Items.Count,
            totalItems: page.TotalItems);

        return MethodResult<PublicEventsPageOutput>.Success(page);
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
            SearchPublicEventsLog.RollbackFailed(_logger, ex);
        }
    }
}
