using Application.UseCases.Base;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetCustomerTickets.Inputs;
using Domain.BoundedContexts.OrderContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetCustomerTickets;

/// <summary>
/// Meus ingressos (REQ13): os ingressos emitidos do comprador (RN4), próximos (evento que não terminou, RN5) ou passados, por data do evento.
/// Consulta por <c>id_customer</c>, então o ingresso de outro cliente nunca aparece (RN1).
/// </summary>
public sealed class GetCustomerTicketsUseCase : IUseCase<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput>
{
    public const string CUSTOMER_NOT_FOUND_NOTIFICATION_CODE = "CUSTOMER_NOT_FOUND";
    private const string CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE = "O cliente da sessão não existe.";

    public const string GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_CODE = "GET_CUSTOMER_TICKETS_CANCELED";
    private const string GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_MESSAGE = "A consulta dos ingressos foi cancelada.";

    private readonly ITicketRepository _ticketRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly GetCustomerTicketsDiagnostics _diagnostics;
    private readonly ILogger<GetCustomerTicketsUseCase> _logger;

    public GetCustomerTicketsUseCase(
        ITicketRepository ticketRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        GetCustomerTicketsDiagnostics diagnostics,
        ILogger<GetCustomerTicketsUseCase> logger)
    {
        _ticketRepository = ticketRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<MethodResult<CustomerTicketsPageOutput>> ExecuteAsync(GetCustomerTicketsUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetCustomerTicketsDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetCustomerTickets", auditableInfo);

        _diagnostics.Requested();

        try
        {
            var validationResult = MethodResult.Factory(input.GetInputValidation(), auditableInfo.MethodResult);

            if (validationResult.IsError)
            {
                _diagnostics.Finished(GetCustomerTicketsDiagnostics.OUTCOME_VALIDATION_FAILED);

                return MethodResult<CustomerTicketsPageOutput>.Error(validationResult.Notifications);
            }

            if (input.CustomerId is null)
            {
                _diagnostics.Finished(GetCustomerTicketsDiagnostics.OUTCOME_CUSTOMER_NOT_FOUND);

                return MethodResult<CustomerTicketsPageOutput>.Error([Notification.Error(CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, CUSTOMER_NOT_FOUND_NOTIFICATION_MESSAGE)]);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            now = now.AddTicks(-(now.Ticks % 10));

            var upcoming = input.Filters.TicketPeriod == TicketPeriod.UPCOMING;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var page = await _ticketRepository.ListByCustomerAsync(input.CustomerId.Value, upcoming, now, input.Page.Offset, input.Page.PageSize, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            var output = new CustomerTicketsPageOutput(
                Items: page.Items.Select(ToItem).ToArray(),
                Page: input.Page.Page,
                PageSize: input.Page.PageSize,
                TotalItems: page.TotalItems);

            GetCustomerTicketsLog.Served(_logger, input.Filters.TicketPeriod, output.Page, output.Items.Count, output.TotalItems);
            _diagnostics.Finished(GetCustomerTicketsDiagnostics.OUTCOME_SERVED);

            return MethodResult<CustomerTicketsPageOutput>.Success(output);
        }
        catch (Exception ex) when (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync();

            _diagnostics.Finished(GetCustomerTicketsDiagnostics.OUTCOME_CANCELED);

            return MethodResult<CustomerTicketsPageOutput>.Error([Notification.Error(GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_CODE, GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_MESSAGE)]);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            GetCustomerTicketsLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            _diagnostics.Finished(GetCustomerTicketsDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    internal static CustomerTicketItemOutput ToItem(CustomerTicketRowModel row)
        => new(
            Code: row.Code,
            Status: row.Status,
            BatchName: row.BatchName,
            HolderName: row.HolderName,
            OrderCode: row.OrderCode,
            Event: ToEvent(row));

    internal static CustomerTicketEventOutput ToEvent(CustomerTicketRowModel row)
        => new(
            Slug: row.EventSlug,
            Title: row.EventTitle,
            StartsAt: DateTime.SpecifyKind(row.EventStartsAt, DateTimeKind.Utc),
            DoorsOpenAt: row.DoorsOpenAt is null ? null : DateTime.SpecifyKind(row.DoorsOpenAt.Value, DateTimeKind.Utc),
            VenueName: row.VenueName,
            Address: row.Address,
            City: row.City,
            State: row.State,
            ImageUrl: row.EventImageUrl);

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GetCustomerTicketsLog.RollbackFailed(_logger, ex);
        }
    }
}
