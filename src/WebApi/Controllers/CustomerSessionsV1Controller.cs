using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomerSession;
using Application.UseCases.CreateCustomerSession.Inputs;
using Application.UseCases.GetCurrentCustomer;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Application.UseCases.RefreshCustomerSession;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Application.UseCases.RevokeCustomerSession;
using Application.UseCases.RevokeCustomerSession.Inputs;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using WebApi.Authentication;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>Sessão do cliente (REQ12): login, renovação e logout (anônimos) e meus dados (política Customer).</summary>
[Route("api/v1/customers")]
[ApiController]
public sealed class CustomerSessionsV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    [HttpPost("sessions")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateCustomerSessionAsync(
        [FromServices] IUseCase<CreateCustomerSessionUseCaseInput, CustomerSessionOutput> useCase,
        [FromServices] LoginAttemptLimiter loginAttemptLimiter,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateCustomerSessionV1Request request,
        CancellationToken cancellationToken = default)
    {
        var input = CreateCustomerSessionUseCaseInput.Create(request.Login, request.Password);
        var result = await useCase.ExecuteAsync(input, AuditableInfoValueObject.Build(correlationId, originService, executionUser, requestedAt), cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(CreateCustomerSessionUseCase.CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (codes.Contains(CreateCustomerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE))
        {
            Response.Headers.RetryAfter = Math.Max(1, loginAttemptLimiter.GetRetryAfterSeconds(input.Login.AttemptKey)).ToString(CultureInfo.InvariantCulture);

            return StatusCode(StatusCodes.Status429TooManyRequests, result.Notifications);
        }

        if (codes.Contains(CreateCustomerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE))
            return Unauthorized(result.Notifications);

        return BadRequest(result.Notifications);
    }

    [HttpPost("sessions/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshCustomerSessionAsync(
        [FromServices] IUseCase<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CustomerRefreshTokenV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(RefreshCustomerSessionUseCaseInput.Create(request.RefreshToken), AuditableInfoValueObject.Build(correlationId, originService, executionUser, requestedAt), cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        if (result.Notifications.Any(notification => notification.Code == RefreshCustomerSessionUseCase.REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == RefreshCustomerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE))
            return Unauthorized(result.Notifications);

        return BadRequest(result.Notifications);
    }

    [HttpPost("sessions/revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeCustomerSessionAsync(
        [FromServices] IUseCase<RevokeCustomerSessionUseCaseInput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CustomerRefreshTokenV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(request.RefreshToken), AuditableInfoValueObject.Build(correlationId, originService, executionUser, requestedAt), cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == RevokeCustomerSessionUseCase.REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return NoContent();
    }

    /// <summary>Meus dados: o cadastro do cliente do token, com o CPF ofuscado.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthenticationExtensions.CUSTOMER_POLICY)]
    public async Task<IActionResult> GetCurrentCustomerAsync(
        [FromServices] IUseCase<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var customerId = User.GetCustomerId();
        var result = await useCase.ExecuteAsync(GetCurrentCustomerUseCaseInput.Create(customerId), AuditableInfoValueObject.Build(correlationId, originService, customerId, requestedAt), cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        if (result.Notifications.Any(notification => notification.Code == GetCurrentCustomerUseCase.GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == GetCurrentCustomerUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        return BadRequest(result.Notifications);
    }
}
