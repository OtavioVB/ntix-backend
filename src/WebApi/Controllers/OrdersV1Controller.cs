using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrder;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.GetCustomerOrder;
using Application.UseCases.GetCustomerOrder.Inputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.GetOrderPaymentMethods;
using Application.UseCases.GetOrderPaymentMethods.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Authentication;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>Pedidos do comprador (REQ6): criação com reserva de ingressos e consulta pelo código. Política Customer.</summary>
[Route("api/v1/orders")]
[ApiController]
[Authorize(Policy = AuthenticationExtensions.CUSTOMER_POLICY)]
public sealed class OrdersV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    /// <summary>Cria o pedido e reserva os ingressos. O <c>ExecutionUser</c> é o id do cliente do token.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrderAsync(
        [FromServices] IUseCase<CreateOrderUseCaseInput, OrderOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateOrderV1Request request,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var input = CreateOrderUseCaseInput.Create(
            customerId: customerId,
            eventSlug: request.EventSlug,
            salesChannel: request.SalesChannel,
            items: request.Items?.Select(item => (item.BatchId, item.Quantity)));

        var result = await useCase.ExecuteAsync(input, AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt), cancellationToken);

        if (!result.IsError)
            return Created($"/api/v1/orders/{result.Output!.Code}", result.Output);

        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(CreateOrderUseCase.CREATE_ORDER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (codes.Contains(CreateOrderUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        if (codes.Contains(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE))
            return Conflict(result.Notifications);

        return BadRequest(result.Notifications);
    }

    /// <summary>Pedido do cliente do token pelo código. O de outro cliente responde 404 (RN11).</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetCustomerOrderAsync(
        [FromServices] IUseCase<GetCustomerOrderUseCaseInput, OrderOutput> useCase,
        [FromRoute] string? code,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(GetCustomerOrderUseCaseInput.Create(code, customerId), AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt), cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        if (result.Notifications.Any(notification => notification.Code == GetCustomerOrderUseCase.GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        return BadRequest(result.Notifications);
    }

    /// <summary>Métodos de pagamento que o checkout pode oferecer para o pedido (REQ7, RN1).</summary>
    [HttpGet("{code}/payment-methods")]
    public async Task<IActionResult> GetOrderPaymentMethodsAsync(
        [FromServices] IUseCase<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput> useCase,
        [FromRoute] string? code,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(GetOrderPaymentMethodsUseCaseInput.Create(code, customerId), AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt), cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        if (result.Notifications.Any(notification => notification.Code == GetOrderPaymentMethodsUseCase.GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == GetOrderPaymentMethodsUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        return BadRequest(result.Notifications);
    }

    /// <summary>Gera o Pix do pedido (201) ou devolve o Pix pendente que já existe (200, RN4).</summary>
    [HttpPost("{code}/payments")]
    public async Task<IActionResult> CreateOrderPaymentAsync(
        [FromServices] IUseCase<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput> useCase,
        [FromRoute] string? code,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateOrderPaymentV1Request request,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(CreateOrderPaymentUseCaseInput.Create(code, customerId, request.Method), AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt), cancellationToken);

        if (!result.IsError)
            return result.Output!.Reused
                ? Ok(result.Output.Payment)
                : Created($"/api/v1/orders/{code}", result.Output.Payment);

        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(CreateOrderPaymentUseCase.CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (codes.Contains(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        if (codes.Overlaps([CreateOrderPaymentUseCase.ORDER_ALREADY_PAID_NOTIFICATION_CODE, CreateOrderPaymentUseCase.ORDER_EXPIRED_NOTIFICATION_CODE, CreateOrderPaymentUseCase.ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE]))
            return Conflict(result.Notifications);

        // Todas as integrações falharam: sem detalhe do fornecedor no corpo.
        if (codes.Contains(CreateOrderPaymentUseCase.PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result.Notifications);

        return BadRequest(result.Notifications);
    }
}
