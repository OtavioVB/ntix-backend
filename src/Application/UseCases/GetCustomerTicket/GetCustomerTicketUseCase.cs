using Application.UseCases.Base;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.GetCustomerTicket.Inputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetCustomerTickets;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Orders.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.UseCases.GetCustomerTicket;

/// <summary>
/// Detalhe do ingresso para a portaria (REQ13, RN6). Consulta por <c>(code, id_customer)</c>: o ingresso de outro cliente responde
/// como inexistente (RN1). O código é o conteúdo do QR Code; a imagem é desenhada no front.
/// </summary>
public sealed class GetCustomerTicketUseCase : IUseCase<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput>
{
    public const string TICKET_NOT_FOUND_NOTIFICATION_CODE = "TICKET_NOT_FOUND";
    private const string TICKET_NOT_FOUND_NOTIFICATION_MESSAGE = "O ingresso informado não existe.";

    public const string GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_CODE = "GET_CUSTOMER_TICKET_CANCELED";
    private const string GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_MESSAGE = "A consulta do ingresso foi cancelada.";

    private readonly ITicketRepository _ticketRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GetCustomerTicketDiagnostics _diagnostics;
    private readonly ILogger<GetCustomerTicketUseCase> _logger;

    public GetCustomerTicketUseCase(
        ITicketRepository ticketRepository,
        IUnitOfWork unitOfWork,
        GetCustomerTicketDiagnostics diagnostics,
        ILogger<GetCustomerTicketUseCase> logger)
    {
        _ticketRepository = ticketRepository;
        _unitOfWork = unitOfWork;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task<MethodResult<CustomerTicketDetailOutput>> ExecuteAsync(GetCustomerTicketUseCaseInput input, AuditableInfoValueObject auditableInfo, CancellationToken cancellationToken = default)
    {
        using var auditableScope = _logger.BeginAuditableScope(auditableInfo);
        using var activity = GetCustomerTicketDiagnostics.ACTIVITY_SOURCE.StartAuditableActivity("GetCustomerTicket", auditableInfo);

        _diagnostics.Requested();

        try
        {
            if (!input.IsValid)
            {
                _diagnostics.Finished(GetCustomerTicketDiagnostics.OUTCOME_NOT_FOUND);

                return NotFound();
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var row = await _ticketRepository.GetByCodeForCustomerAsync(input.Code, input.CustomerId, cancellationToken);

            await _unitOfWork.RollbackAsync(CancellationToken.None);

            if (row is null)
            {
                GetCustomerTicketLog.NotFound(_logger, input.Code);
                _diagnostics.Finished(GetCustomerTicketDiagnostics.OUTCOME_NOT_FOUND);

                return NotFound();
            }

            var output = new CustomerTicketDetailOutput(
                Code: row.Code,
                Status: row.Status,
                BatchName: row.BatchName,
                HolderName: row.HolderName,
                OrderCode: row.OrderCode,
                Event: GetCustomerTicketsUseCase.ToEvent(row),
                AgeRating: row.AgeRating,
                IssuedAt: DateTime.SpecifyKind(row.IssuedAt, DateTimeKind.Utc));

            _diagnostics.Finished(GetCustomerTicketDiagnostics.OUTCOME_SERVED);

            return MethodResult<CustomerTicketDetailOutput>.Success(output);
        }
        catch (Exception ex) when (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync();

            _diagnostics.Finished(GetCustomerTicketDiagnostics.OUTCOME_CANCELED);

            return MethodResult<CustomerTicketDetailOutput>.Error([Notification.Error(GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_CODE, GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_MESSAGE)]);
        }
        catch (Exception ex)
        {
            await RollbackAsync();

            GetCustomerTicketLog.Failed(_logger, ex);

            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

            _diagnostics.Finished(GetCustomerTicketDiagnostics.OUTCOME_ERROR);

            throw;
        }
    }

    private static MethodResult<CustomerTicketDetailOutput> NotFound()
        => MethodResult<CustomerTicketDetailOutput>.Error([Notification.Error(TICKET_NOT_FOUND_NOTIFICATION_CODE, TICKET_NOT_FOUND_NOTIFICATION_MESSAGE)]);

    private async Task RollbackAsync()
    {
        try
        {
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            GetCustomerTicketLog.RollbackFailed(_logger, ex);
        }
    }
}
