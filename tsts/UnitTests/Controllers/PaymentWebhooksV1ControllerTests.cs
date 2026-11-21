using Application.UseCases.Base.Interfaces;
using Application.UseCases.ConfirmPaymentFromWebhook;
using Application.UseCases.ConfirmPaymentFromWebhook.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Domain.ValueObjects;
using NSubstitute;
using System.Text;
using UnitTests.Support;
using WebApi.Controllers;

namespace UnitTests.Controllers;

/// <summary>Tradução do resultado do webhook (REQ8) para o HTTP: sem corpo, com o status de cada desfecho e o limite de 64 KB.</summary>
public sealed class PaymentWebhooksV1ControllerTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;
    private const int MAX_BODY_BYTES = 64 * 1024;

    private readonly IUseCase<ConfirmPaymentFromWebhookUseCaseInput> _useCase = Substitute.For<IUseCase<ConfirmPaymentFromWebhookUseCaseInput>>();
    private readonly DefaultHttpContext _httpContext = new();
    private readonly PaymentWebhooksV1Controller _controller;

    public PaymentWebhooksV1ControllerTests()
    {
        _controller = new PaymentWebhooksV1Controller { ControllerContext = new ControllerContext { HttpContext = _httpContext } };
    }

    private void GivenBody(byte[] body)
    {
        _httpContext.Request.Body = new MemoryStream(body);
        _httpContext.Request.ContentLength = body.Length;
    }

    private void GivenUseCaseRefuses(string code)
        => _useCase.ExecuteAsync(Arg.Any<ConfirmPaymentFromWebhookUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MethodResult.Error([Notification.Error(code, "mensagem")])));

    private void GivenUseCaseAccepts()
        => _useCase.ExecuteAsync(Arg.Any<ConfirmPaymentFromWebhookUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MethodResult.Success()));

    private static byte[] Envelope()
        => Encoding.UTF8.GetBytes("{\"id\":\"log_1\",\"event\":\"transparent.completed\",\"data\":{\"transparent\":{\"id\":\"pix_char_abc\"}}}");

    // As respostas sem corpo gravam o status no Response (EmptyResult); o OkResult carrega o próprio status.
    private int StatusOf(IActionResult result)
        => result is StatusCodeResult statusCodeResult ? statusCodeResult.StatusCode : _httpContext.Response.StatusCode;

    [Fact]
    public async Task Given_ConfirmedWebhook_When_Receiving_Should_RespondOkWithoutBody()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseAccepts();

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Given_RawBodyAndHeaders_When_Receiving_Should_PassThemUnchangedToTheUseCase()
    {
        // Arrange
        var body = Envelope();
        GivenBody(body);
        ConfirmPaymentFromWebhookUseCaseInput? captured = null;
        _useCase.ExecuteAsync(Arg.Do<ConfirmPaymentFromWebhookUseCaseInput>(input => captured = input), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MethodResult.Success()));

        // Act
        await _controller.AbacatePayAsync(_useCase, "assinatura", "segredo");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(body, captured!.RawBody);
        Assert.Equal("assinatura", captured.Signature);
        Assert.Equal("segredo", captured.Secret);
    }

    [Fact]
    public async Task Given_BodyAboveTheLimitWithContentLength_When_Receiving_Should_RespondBadRequestWithoutCallingTheUseCase()
    {
        // Arrange
        GivenBody(new byte[MAX_BODY_BYTES + 1]);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        await _useCase.DidNotReceive().ExecuteAsync(Arg.Any<ConfirmPaymentFromWebhookUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_BodyAboveTheLimitWithoutContentLength_When_Receiving_Should_RespondBadRequestAfterReadingTheLimit()
    {
        // Arrange
        _httpContext.Request.Body = new MemoryStream(new byte[MAX_BODY_BYTES + 10]);
        _httpContext.Request.ContentLength = null;

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Given_SignatureRefused_When_Receiving_Should_RespondUnauthorized()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.WEBHOOK_SIGNATURE_INVALID_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task Given_SecretRefused_When_Receiving_Should_RespondUnauthorized()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.WEBHOOK_SECRET_INVALID_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task Given_BodyNotReadable_When_Receiving_Should_RespondBadRequest()
    {
        // Arrange
        GivenBody(Encoding.UTF8.GetBytes("não é json"));
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.WEBHOOK_BODY_INVALID_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Given_PaymentNotFound_When_Receiving_Should_RespondNotFound()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.PAYMENT_NOT_FOUND_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Given_SourceUnavailable_When_Receiving_Should_RespondServiceUnavailableSoTheProviderRetries()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.SOURCE_UNAVAILABLE_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusOf(result));
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Receiving_Should_RespondClientClosedRequest()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses(ConfirmPaymentFromWebhookUseCase.CONFIRM_PAYMENT_CANCELED_NOTIFICATION_CODE);

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(CLIENT_CLOSED_REQUEST, StatusOf(result));
    }

    [Fact]
    public async Task Given_UnmappedNotification_When_Receiving_Should_RespondInternalServerError()
    {
        // Arrange
        GivenBody(Envelope());
        GivenUseCaseRefuses("CODIGO_NAO_MAPEADO");

        // Act
        var result = await _controller.AbacatePayAsync(_useCase, "sig", "secret");

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, StatusOf(result));
    }
}
