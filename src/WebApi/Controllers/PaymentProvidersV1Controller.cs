using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetPaymentProviders;
using Application.UseCases.GetPaymentProviders.Inputs;
using Application.UseCases.GetPaymentProviders.Outputs;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[Route("api/v1/payment-providers")]
[ApiController]
public sealed class PaymentProvidersV1Controller : ControllerBase
{
    private const int CLIENT_CLOSED_REQUEST_STATUS_CODE = 499;

    /// <summary>Catálogo dos fornecedores de pagamento habilitados, com métodos e credenciais exigidas. Público.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetPaymentProvidersAsync(
        [FromServices] IUseCase<GetPaymentProvidersUseCaseInput, GetPaymentProvidersUseCaseOutput> useCase,
        [FromHeader(Name = AuditableInfoValueObject.CORRELATION_ID_HEADER_KEY)] string? correlationId,
        [FromHeader(Name = AuditableInfoValueObject.ORIGIN_SERVICE_HEADER_KEY)] string? originService,
        [FromHeader(Name = AuditableInfoValueObject.EXECUTION_USER_HEADER_KEY)] string? executionUser,
        [FromHeader(Name = AuditableInfoValueObject.REQUESTED_AT_HEADER_KEY)] string? requestedAt,
        CancellationToken cancellationToken = default)
    {
        var result = await useCase.ExecuteAsync(
            input: GetPaymentProvidersUseCaseInput.Create(),
            auditableInfo: AuditableInfoValueObject.Build(
                correlationId: correlationId,
                originService: originService,
                executionUser: executionUser,
                requestedAt: requestedAt),
            cancellationToken: cancellationToken);

        if (result.Notifications.Any(notification => notification.Code == GetPaymentProvidersUseCase.GET_PAYMENT_PROVIDERS_CANCELED_NOTIFICATION_CODE))
            return StatusCode(CLIENT_CLOSED_REQUEST_STATUS_CODE, result.Notifications);

        if (result.IsError)
            return BadRequest(result.Notifications);

        return Ok(result.Output!.Providers);
    }
}
