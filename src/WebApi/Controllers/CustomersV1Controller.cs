using Application.UseCases.Base.Interfaces;
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
                document: request.Document),
            auditableInfo: AuditableInfoValueObject.Build(
                correlationId: correlationId,
                originService: originService,
                executionUser: executionUser,
                requestedAt: requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return Ok(result.Notifications);
    }
}
