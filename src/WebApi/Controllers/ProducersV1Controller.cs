using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

[Route("api/v1/producers")]
[ApiController]
public sealed class ProducersV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> CreateProducerAsync(
        [FromServices] IUseCase<CreateProducerUseCaseInput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateProducerV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: CreateProducerUseCaseInput.Create(
                name: request.Name,
                email: request.Email,
                phone: request.Phone,
                document: request.Document,
                userName: request.UserName,
                password: request.Password),
            auditableInfo: AuditableInfoValueObject.Build(
                correlationId: correlationId,
                originService: originService,
                executionUser: executionUser,
                requestedAt: requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == CreateProducerUseCase.CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return Ok(result.Notifications);
    }
}
