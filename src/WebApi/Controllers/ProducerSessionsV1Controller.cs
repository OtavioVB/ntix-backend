using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>
/// Sessão do usuário do produtor (REQ5): login, renovação e logout. Os três são anônimos: quem chama ainda não tem, ou
/// já não tem, um access token válido.
/// </summary>
[Route("api/v1/producers/sessions")]
[ApiController]
[AllowAnonymous]
public sealed class ProducerSessionsV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    /// <summary>Login com e-mail e senha. Devolve access token (15 min), refresh token (7 dias), usuário e produtora.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateProducerSessionAsync(
        [FromServices] IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput> useCase,
        [FromServices] LoginAttemptLimiter loginAttemptLimiter,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateProducerSessionV1Request request,
        CancellationToken cancellationToken = default)
    {
        var input = CreateProducerSessionUseCaseInput.Create(email: request.Email, password: request.Password);

        var result = await useCase.ExecuteAsync(
            input: input,
            auditableInfo: BuildAuditableInfo(correlationId, originService, executionUser, requestedAt),
            cancellationToken: cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(CreateProducerSessionUseCase.CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (codes.Contains(CreateProducerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE))
        {
            // RN12: quando tentar de novo, em segundos até o fim da janela do e-mail.
            Response.Headers.RetryAfter = Math.Max(1, loginAttemptLimiter.GetRetryAfterSeconds(input.Email)).ToString(System.Globalization.CultureInfo.InvariantCulture);

            return StatusCode(StatusCodes.Status429TooManyRequests, result.Notifications);
        }

        if (codes.Contains(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE))
            return Unauthorized(result.Notifications);

        if (codes.Contains(CreateProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE))
            return StatusCode(StatusCodes.Status403Forbidden, result.Notifications);

        return BadRequest(result.Notifications);
    }

    /// <summary>Troca o refresh token por um par novo. O token usado deixa de funcionar; reapresentá-lo encerra a sessão.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshProducerSessionAsync(
        [FromServices] IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] RefreshProducerSessionV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: RefreshProducerSessionUseCaseInput.Create(request.RefreshToken),
            auditableInfo: BuildAuditableInfo(correlationId, originService, executionUser, requestedAt),
            cancellationToken: cancellationToken);

        if (!result.IsError)
            return Ok(result.Output);

        var codes = result.Notifications.Select(notification => notification.Code).ToHashSet(StringComparer.Ordinal);

        if (codes.Contains(RefreshProducerSessionUseCase.REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (codes.Contains(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE))
            return Unauthorized(result.Notifications);

        if (codes.Contains(RefreshProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE))
            return StatusCode(StatusCodes.Status403Forbidden, result.Notifications);

        return BadRequest(result.Notifications);
    }

    /// <summary>Logout: revoga a família inteira do refresh token. 204 também para token já revogado ou inexistente.</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeProducerSessionAsync(
        [FromServices] IUseCase<RevokeProducerSessionUseCaseInput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] RevokeProducerSessionV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: RevokeProducerSessionUseCaseInput.Create(request.RefreshToken),
            auditableInfo: BuildAuditableInfo(correlationId, originService, executionUser, requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == RevokeProducerSessionUseCase.REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return NoContent();
    }

    private static AuditableInfoValueObject BuildAuditableInfo(string? correlationId, string? originService, string? executionUser, string? requestedAt)
        => AuditableInfoValueObject.Build(
            correlationId: correlationId,
            originService: originService,
            executionUser: executionUser,
            requestedAt: requestedAt);
}
