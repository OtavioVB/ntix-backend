using Application.UseCases.Base.Events.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetPublicEvent;
using Application.UseCases.GetPublicEvent.Inputs;
using Application.UseCases.SearchPublicEvents;
using Application.UseCases.SearchPublicEvents.Inputs;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

/// <summary>
/// Vitrine pública (REQ11): lista do hub e da loja (<c>?producer={slug}</c>) e página do evento. Anônima, com cache público
/// curto (RN8); o pedido (REQ6) revalida a disponibilidade com trava, então a defasagem não vende acima do estoque.
/// </summary>
[Route("api/v1/events")]
[ApiController]
[AllowAnonymous]
public sealed class EventsV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;
    private const string CACHE_CONTROL = "public, max-age=15";

    [HttpGet]
    public async Task<IActionResult> SearchPublicEventsAsync(
        [FromServices] IUseCase<SearchPublicEventsUseCaseInput, PublicEventsPageOutput> useCase,
        [FromQuery] string? producer,
        [FromQuery] string? category,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] string? startsFrom,
        [FromQuery] string? startsTo,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: SearchPublicEventsUseCaseInput.Create(producer, category, city, state, startsFrom, startsTo, page, pageSize),
            auditableInfo: AuditableInfoValueObject.Build(correlationId, originService, executionUser, requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == SearchPublicEventsUseCase.SEARCH_PUBLIC_EVENTS_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        Response.Headers.CacheControl = CACHE_CONTROL;

        return Ok(result.Output);
    }

    [HttpGet("{eventSlug}")]
    public async Task<IActionResult> GetPublicEventAsync(
        [FromServices] IUseCase<GetPublicEventUseCaseInput, PublicEventOutput> useCase,
        [FromRoute] string? eventSlug,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetPublicEventUseCaseInput.Create(eventSlug),
            auditableInfo: AuditableInfoValueObject.Build(correlationId, originService, executionUser, requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == GetPublicEventUseCase.GET_PUBLIC_EVENT_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.Notifications.Any(notification => notification.Code == GetPublicEventUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE))
            return NotFound(result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        Response.Headers.CacheControl = CACHE_CONTROL;

        return Ok(result.Output);
    }
}
