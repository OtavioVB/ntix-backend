using Application.UseCases.AuthenticateCustomer;
using Application.UseCases.AuthenticateCustomer.Inputs;
using Application.UseCases.AuthenticateCustomer.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
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
    private const string BEARER_TOKEN_TYPE = "Bearer";

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

    [HttpPost("authentication")]
    [AllowAnonymous]
    public async Task<IActionResult> AuthenticateCustomerAsync(
        [FromServices] IUseCase<AuthenticateCustomerUseCaseInput, AuthenticateCustomerUseCaseOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] AuthenticateCustomerV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: AuthenticateCustomerUseCaseInput.Create(
                email: request.Email,
                document: request.Document,
                password: request.Password,
                producerId: request.ProducerId),
            auditableInfo: AuditableInfoValueObject.Build(
                correlationId: correlationId,
                originService: originService,
                executionUser: executionUser,
                requestedAt: requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == AuthenticateCustomerUseCase.AUTHENTICATE_CUSTOMER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == AuthenticateCustomerUseCase.AUTHENTICATION_FAILED_NOTIFICATION_CODE))
            return Unauthorized(result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return Ok(new AuthenticateCustomerV1Response(
            AccessToken: result.Output!.AccessToken,
            TokenType: BEARER_TOKEN_TYPE,
            ExpiresAt: result.Output.ExpiresAt));
    }
}
