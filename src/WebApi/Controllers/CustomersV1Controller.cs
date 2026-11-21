using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.GetCustomerOrders;
using Application.UseCases.GetCustomerOrders.Inputs;
using Application.UseCases.GetCustomerTicket;
using Application.UseCases.GetCustomerTicket.Inputs;
using Application.UseCases.GetCustomerTickets;
using Application.UseCases.GetCustomerTickets.Inputs;
using Domain.CrossCutting.NotificationContext.Interfaces;
using WebApi.Authentication;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

[Route("api/v1/customers")]
[ApiController]
public sealed class CustomersV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> CreateCustomerAsync(
        [FromServices] IUseCase<CreateCustomerUseCaseInput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateCustomerV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: CreateCustomerUseCaseInput.Create(
                fullName: request.FullName,
                email: request.Email,
                document: request.Document,
                password: request.Password,
                phoneNumber: request.PhoneNumber),
            auditableInfo: AuditableInfoValueObject.Build(
                correlationId: correlationId,
                originService: originService,
                executionUser: executionUser,
                requestedAt: requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == CreateCustomerUseCase.CREATE_CUSTOMER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return Ok(result.Notifications);
    }

    /// <summary>Meus pedidos (REQ13): do mais recente ao mais antigo, com o status efetivo (RN3). Política Customer.</summary>
    [HttpGet("me/orders")]
    [Authorize(Policy = AuthenticationExtensions.CUSTOMER_POLICY)]
    public async Task<IActionResult> GetMyOrdersAsync(
        [FromServices] IUseCase<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput> useCase,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(
            GetCustomerOrdersUseCaseInput.Create(customerId, status, page, pageSize),
            AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt),
            cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        return ListError(result.Notifications, GetCustomerOrdersUseCase.GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_CODE, GetCustomerOrdersUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE);
    }

    /// <summary>Meus ingressos (REQ13): próximos (padrão) ou passados, por data do evento. Política Customer.</summary>
    [HttpGet("me/tickets")]
    [Authorize(Policy = AuthenticationExtensions.CUSTOMER_POLICY)]
    public async Task<IActionResult> GetMyTicketsAsync(
        [FromServices] IUseCase<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput> useCase,
        [FromQuery] string? when,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(
            GetCustomerTicketsUseCaseInput.Create(customerId, when, page, pageSize),
            AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt),
            cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        return ListError(result.Notifications, GetCustomerTicketsUseCase.GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_CODE, GetCustomerTicketsUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE);
    }

    /// <summary>Detalhe de um ingresso do cliente (REQ13, RN6): o código é o conteúdo do QR Code. O de outro cliente responde 404 (RN1).</summary>
    [HttpGet("me/tickets/{code}")]
    [Authorize(Policy = AuthenticationExtensions.CUSTOMER_POLICY)]
    public async Task<IActionResult> GetMyTicketAsync(
        [FromServices] IUseCase<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput> useCase,
        [FromRoute] string? code,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(
            GetCustomerTicketUseCaseInput.Create(code, customerId),
            AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt),
            cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        if (result.Notifications.Any(notification => notification.Code == GetCustomerTicketUseCase.GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        return NotFound(result.Notifications);
    }

    private IActionResult ListError(IReadOnlyList<INotification> notifications, string canceledCode, string customerNotFoundCode)
    {
        if (notifications.Any(notification => notification.Code == canceledCode))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, notifications);

        if (notifications.Any(notification => notification.Code == customerNotFoundCode))
            return NotFound(notifications);

        return BadRequest(notifications);
    }
}
