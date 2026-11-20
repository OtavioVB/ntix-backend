using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateEvent;
using Application.UseCases.CreateEvent.Inputs;
using Application.UseCases.GetProducerEvent;
using Application.UseCases.GetProducerEvent.Inputs;
using Application.UseCases.GetProducerEvents;
using Application.UseCases.GetProducerEvents.Inputs;
using Application.UseCases.GetProducerEvents.Outputs;
using Application.UseCases.UpdateEvent;
using Application.UseCases.UpdateEvent.Inputs;
using Application.UseCases.PublishEvent;
using Application.UseCases.PublishEvent.Inputs;
using Domain.BoundedContexts.EventContext.Entities;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Authentication;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>
/// Aba Eventos do painel do produtor (REQ9): eventos em rascunho, lista, consulta e edição.
/// Só atende o usuário da própria produtora (RN1): 401 sem token válido e 403 com token de outra produtora.
/// </summary>
[Route("api/v1/producers/{slug}/events")]
[ApiController]
[Authorize(Policy = AuthenticationExtensions.PRODUCER_USER_POLICY)]
public sealed class ProducerEventsV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    /// <summary>Cria o evento em rascunho, com slug gerado do título.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateEventAsync(
        [FromServices] IUseCase<CreateEventUseCaseInput, ProducerEventOutput> useCase,
        [FromRoute] string? slug,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] EventV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: CreateEventUseCaseInput.Create(
                producerSlug: slug,
                title: request.Title,
                description: request.Description,
                category: request.Category,
                startsAt: request.StartsAt,
                endsAt: request.EndsAt,
                doorsOpenAt: request.DoorsOpenAt,
                venueName: request.VenueName,
                address: request.Address,
                city: request.City,
                state: request.State,
                ageRating: request.AgeRating,
                imageUrl: request.ImageUrl),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: CreateEventUseCase.CREATE_EVENT_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [CreateEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE]);
        }

        return Created($"/api/v1/producers/{slug}/events/{result.Output!.Id}", result.Output);
    }

    /// <summary>Eventos da produtora por início crescente; <c>status</c> opcional (DRAFT ou PUBLISHED).</summary>
    [HttpGet]
    public async Task<IActionResult> GetProducerEventsAsync(
        [FromServices] IUseCase<GetProducerEventsUseCaseInput, GetProducerEventsUseCaseOutput> useCase,
        [FromRoute] string? slug,
        [FromQuery] string? status,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetProducerEventsUseCaseInput.Create(slug, status),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: GetProducerEventsUseCase.GET_PRODUCER_EVENTS_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [GetProducerEventsUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE]);
        }

        return Ok(result.Output!.Events);
    }

    /// <summary>Um evento da produtora. Evento de outra produtora responde 404.</summary>
    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetProducerEventAsync(
        [FromServices] IUseCase<GetProducerEventUseCaseInput, ProducerEventOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetProducerEventUseCaseInput.Create(slug, eventId),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: GetProducerEventUseCase.GET_PRODUCER_EVENT_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [GetProducerEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, GetProducerEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE]);
        }

        return Ok(result.Output);
    }

    /// <summary>Substitui os campos editáveis, até o início do evento. O slug não muda.</summary>
    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEventAsync(
        [FromServices] IUseCase<UpdateEventUseCaseInput, ProducerEventOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] EventV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: UpdateEventUseCaseInput.Create(
                producerSlug: slug,
                eventId: eventId,
                title: request.Title,
                description: request.Description,
                category: request.Category,
                startsAt: request.StartsAt,
                endsAt: request.EndsAt,
                doorsOpenAt: request.DoorsOpenAt,
                venueName: request.VenueName,
                address: request.Address,
                city: request.City,
                state: request.State,
                ageRating: request.AgeRating,
                imageUrl: request.ImageUrl),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: UpdateEventUseCase.UPDATE_EVENT_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [UpdateEventUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, UpdateEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE]);
        }

        return Ok(result.Output);
    }

    /// <summary>Publica o evento (REQ10): exige rascunho, lote em venda futura e um método de pagamento disponível.</summary>
    [HttpPost("{eventId}/publication")]
    public async Task<IActionResult> PublishEventAsync(
        [FromServices] IUseCase<PublishEventUseCaseInput, ProducerEventOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? eventId,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: PublishEventUseCaseInput.Create(slug, eventId),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            if (result.Notifications.Any(notification => notification.Code == Event.EVENT_NOT_DRAFT_NOTIFICATION_CODE))
                return Conflict(result.Notifications);

            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: PublishEventUseCase.PUBLISH_EVENT_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [PublishEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE]);
        }

        return Ok(result.Output);
    }

    // ExecutionUser vem do token (sub), nunca do header X-Execution-User.
    private AuditableInfoValueObject BuildAuditableInfo(string? correlationId, string? originService, string? requestedAt)
        => AuditableInfoValueObject.Build(
            correlationId: correlationId,
            originService: originService,
            executionUser: User.GetProducerUserId(),
            requestedAt: requestedAt);

    private ObjectResult ToErrorResult(INotification[] notifications, string canceledCode, string[] notFoundCodes)
    {
        if (notifications.Any(notification => notification.Code == canceledCode))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, notifications);

        if (notifications.Any(notification => notFoundCodes.Contains(notification.Code)))
            return NotFound(notifications);

        return BadRequest(notifications);
    }
}
