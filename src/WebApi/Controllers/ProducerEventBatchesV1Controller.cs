using Application.UseCases.Base.Events;
using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateTicketBatch;
using Application.UseCases.CreateTicketBatch.Inputs;
using Application.UseCases.GetTicketBatches;
using Application.UseCases.GetTicketBatches.Inputs;
using Application.UseCases.GetTicketBatches.Outputs;
using Application.UseCases.UpdateTicketBatch;
using Application.UseCases.UpdateTicketBatch.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Authentication;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>
/// Lotes dos eventos no painel do produtor (REQ10): criar, listar com a disponibilidade e editar (inclusive pausar).
/// Só atende o usuário da própria produtora: 401 sem token válido e 403 com token de outra produtora.
/// </summary>
[Route("api/v1/producers/{slug}/events/{eventId}/batches")]
[ApiController]
[Authorize(Policy = AuthenticationExtensions.PRODUCER_USER_POLICY)]
public sealed class ProducerEventBatchesV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    [HttpPost]
    public async Task<IActionResult> CreateTicketBatchAsync(
        [FromServices] IUseCase<CreateTicketBatchUseCaseInput, TicketBatchOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] TicketBatchV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: CreateTicketBatchUseCaseInput.Create(slug, eventId, ToDetails(request)),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: CreateTicketBatchUseCase.CREATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [CreateTicketBatchUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: [CreateTicketBatchUseCase.BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE]);
        }

        return Created($"/api/v1/producers/{slug}/events/{eventId}/batches/{result.Output!.Id}", result.Output);
    }

    /// <summary>Lotes por início da venda e preço, com vendidos, reservados, disponíveis e o estado de venda.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTicketBatchesAsync(
        [FromServices] IUseCase<GetTicketBatchesUseCaseInput, GetTicketBatchesUseCaseOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetTicketBatchesUseCaseInput.Create(slug, eventId),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: GetTicketBatchesUseCase.GET_TICKET_BATCHES_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [GetTicketBatchesUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: []);
        }

        return Ok(result.Output!.Batches);
    }

    [HttpPut("{batchId}")]
    public async Task<IActionResult> UpdateTicketBatchAsync(
        [FromServices] IUseCase<UpdateTicketBatchUseCaseInput, TicketBatchOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromRoute] string? batchId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] TicketBatchV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: UpdateTicketBatchUseCaseInput.Create(slug, eventId, batchId, ToDetails(request)),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: UpdateTicketBatchUseCase.UPDATE_TICKET_BATCH_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [UpdateTicketBatchUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, UpdateTicketBatchUseCase.BATCH_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: [UpdateTicketBatchUseCase.BATCH_NAME_ALREADY_EXISTS_NOTIFICATION_CODE]);
        }

        return Ok(result.Output);
    }

    private static TicketBatchDetails ToDetails(TicketBatchV1Request request)
        => TicketBatchConversions.BuildDetails(request.Name, request.Note, request.PriceCents, request.Quantity, request.SalesStartsAt, request.SalesEndsAt, request.Status);

    // ExecutionUser vem do token (sub), nunca do header X-Execution-User.
    private AuditableInfoValueObject BuildAuditableInfo(string? correlationId, string? originService, string? requestedAt)
        => AuditableInfoValueObject.Build(
            correlationId: correlationId,
            originService: originService,
            executionUser: User.GetProducerUserId(),
            requestedAt: requestedAt);

    private ObjectResult ToErrorResult(INotification[] notifications, string canceledCode, string[] notFoundCodes, string[] conflictCodes)
    {
        if (notifications.Any(notification => notification.Code == canceledCode))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, notifications);

        if (notifications.Any(notification => notFoundCodes.Contains(notification.Code)))
            return NotFound(notifications);

        if (notifications.Any(notification => conflictCodes.Contains(notification.Code)))
            return Conflict(notifications);

        return BadRequest(notifications);
    }
}
