using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateProducerIntegration;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.GetPaymentRoutes;
using Application.UseCases.GetPaymentRoutes.Inputs;
using Application.UseCases.GetPaymentRoutes.Outputs;
using Application.UseCases.GetProducerIntegrations;
using Application.UseCases.GetProducerIntegrations.Inputs;
using Application.UseCases.GetProducerIntegrations.Outputs;
using Application.UseCases.ReplacePaymentRoute;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Application.UseCases.UpdateProducerIntegration;
using Application.UseCases.UpdateProducerIntegration.Inputs;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Authentication;
using WebApi.Controllers.Payloads;

namespace WebApi.Controllers;

/// <summary>
/// Aba Integrações do painel do produtor (REQ4): integrações com os fornecedores de pagamento e rotas por método.
/// Só atende o usuário da própria produtora (RN1): 401 sem token válido e 403 com token de outra produtora.
/// </summary>
[Route("api/v1/producers/{slug}")]
[ApiController]
[Authorize(Policy = AuthenticationExtensions.PRODUCER_USER_POLICY)]
public sealed class ProducerPaymentsV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    /// <summary>Conecta um fornecedor do catálogo, com as credenciais da conta da produtora.</summary>
    [HttpPost("payment-integrations")]
    public async Task<IActionResult> CreateProducerIntegrationAsync(
        [FromServices] IUseCase<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput> useCase,
        [FromRoute] string? slug,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] CreateProducerIntegrationV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: CreateProducerIntegrationUseCaseInput.Create(
                producerSlug: slug,
                providerCode: request.ProviderCode,
                name: request.Name,
                credentials: request.Credentials),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: CreateProducerIntegrationUseCase.CREATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [CreateProducerIntegrationUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: [CreateProducerIntegrationUseCase.INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE]);
        }

        return Created($"/api/v1/producers/{slug}/payment-integrations/{result.Output!.Id}", result.Output);
    }

    /// <summary>Integrações da produtora; credencial secreta só pelos últimos caracteres (<c>valueHint</c>).</summary>
    [HttpGet("payment-integrations")]
    public async Task<IActionResult> GetProducerIntegrationsAsync(
        [FromServices] IUseCase<GetProducerIntegrationsUseCaseInput, GetProducerIntegrationsUseCaseOutput> useCase,
        [FromRoute] string? slug,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetProducerIntegrationsUseCaseInput.Create(slug),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: GetProducerIntegrationsUseCase.GET_PRODUCER_INTEGRATIONS_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [GetProducerIntegrationsUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: []);
        }

        return Ok(result.Output!.Integrations);
    }

    /// <summary>Altera nome, situação e credenciais (parciais) de uma integração. Integração não é excluída, só inativada.</summary>
    [HttpPut("payment-integrations/{id}")]
    public async Task<IActionResult> UpdateProducerIntegrationAsync(
        [FromServices] IUseCase<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? id,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] UpdateProducerIntegrationV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: UpdateProducerIntegrationUseCaseInput.Create(
                producerSlug: slug,
                integrationId: id,
                name: request.Name,
                status: request.Status,
                credentials: request.Credentials),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: UpdateProducerIntegrationUseCase.UPDATE_PRODUCER_INTEGRATION_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [UpdateProducerIntegrationUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE, UpdateProducerIntegrationUseCase.INTEGRATION_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: [UpdateProducerIntegrationUseCase.INTEGRATION_NAME_ALREADY_EXISTS_NOTIFICATION_CODE]);
        }

        return Ok(result.Output);
    }

    /// <summary>Os três métodos, sempre presentes, com as integrações em ordem de prioridade.</summary>
    [HttpGet("payment-routes")]
    public async Task<IActionResult> GetPaymentRoutesAsync(
        [FromServices] IUseCase<GetPaymentRoutesUseCaseInput, GetPaymentRoutesUseCaseOutput> useCase,
        [FromRoute] string? slug,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetPaymentRoutesUseCaseInput.Create(slug),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: GetPaymentRoutesUseCase.GET_PAYMENT_ROUTES_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [GetPaymentRoutesUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: []);
        }

        return Ok(result.Output!.Routes);
    }

    /// <summary>Substitui a lista inteira do método; a ordem do array é a prioridade. Lista vazia remove o método.</summary>
    [HttpPut("payment-routes/{paymentMethod}")]
    public async Task<IActionResult> ReplacePaymentRouteAsync(
        [FromServices] IUseCase<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput> useCase,
        [FromRoute] string? slug,
        [FromRoute] string? paymentMethod,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        [FromBody] ReplacePaymentRouteV1Request request,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: ReplacePaymentRouteUseCaseInput.Create(
                producerSlug: slug,
                paymentMethod: paymentMethod,
                integrationIds: request.IntegrationIds),
            auditableInfo: BuildAuditableInfo(correlationId, originService, requestedAt),
            cancellationToken: cancellationToken);

        if (result.IsError)
        {
            return ToErrorResult(
                notifications: result.Notifications,
                canceledCode: ReplacePaymentRouteUseCase.REPLACE_PAYMENT_ROUTE_CANCELED_NOTIFICATION_CODE,
                notFoundCodes: [ReplacePaymentRouteUseCase.PRODUCER_NOT_FOUND_NOTIFICATION_CODE],
                conflictCodes: []);
        }

        return Ok(result.Output!.Integrations);
    }

    // ExecutionUser vem do token (sub), nunca do header X-Execution-User: a alteração fica atribuída a quem se autenticou.
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
