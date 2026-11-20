using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrderPayment;
using Application.UseCases.CreateOrderPayment.Inputs;
using Application.UseCases.GetOrderPaymentMethods;
using Application.UseCases.GetOrderPaymentMethods.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;
using UnitTests.Support;
using WebApi.Controllers;
using WebApi.Controllers.Payloads;

namespace UnitTests.Controllers;

/// <summary>Tradução do pagamento do pedido (REQ7) para o HTTP do contrato.</summary>
public sealed class OrderPaymentsV1ControllerTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;
    private const string CORRELATION_ID = ProducerSessionTestData.CORRELATION_ID;
    private const string REQUESTED_AT = ProducerSessionTestData.REQUESTED_AT;

    private static readonly OrderPaymentOutput PAYMENT = new(Guid.NewGuid(), "PIX", "PENDING", 24000, OrderTestData.NOW.AddMinutes(13), new PixOutput("00020101", "data:image/png;base64,AAAA"));

    private readonly IUseCase<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput> _methodsUseCase = Substitute.For<IUseCase<GetOrderPaymentMethodsUseCaseInput, PaymentMethodsOutput>>();
    private readonly IUseCase<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput> _paymentUseCase = Substitute.For<IUseCase<CreateOrderPaymentUseCaseInput, CreateOrderPaymentOutput>>();
    private readonly OrdersV1Controller _controller;

    private AuditableInfoValueObject? _auditableInfo;
    private CreateOrderPaymentUseCaseInput? _paymentInput;

    public OrderPaymentsV1ControllerTests()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomerClaims.SUBJECT, OrderTestData.CUSTOMER_ID.ToString()), new Claim(CustomerClaims.ACTOR, CustomerClaims.CUSTOMER_ACTOR)], "Bearer")),
        };

        _controller = new OrdersV1Controller { ControllerContext = new ControllerContext { HttpContext = httpContext } };
    }

    private static INotification[] Error(string code)
        => [Notification.Error(code, "mensagem")];

    private static int? StatusOf(IActionResult result)
        => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => null,
        };

    private Task<IActionResult> PayAsync(string? method = "PIX")
        => _controller.CreateOrderPaymentAsync(_paymentUseCase, "NX7K2QH9MP", CORRELATION_ID, "ntix-web", REQUESTED_AT, new CreateOrderPaymentV1Request(method));

    [Theory]
    [InlineData(null, 200)]
    [InlineData(GetOrderPaymentMethodsUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, 404)]
    [InlineData(GetOrderPaymentMethodsUseCase.GET_ORDER_PAYMENT_METHODS_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", 400)]
    public async Task Given_MethodsResult_When_Answering_Should_ReturnTheStatusAndUseTheTokenAsExecutionUser(string? code, int expectedStatus)
    {
        // Arrange
        _methodsUseCase.ExecuteAsync(Arg.Any<GetOrderPaymentMethodsUseCaseInput>(), Arg.Do<AuditableInfoValueObject>(info => _auditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(code is null ? MethodResult<PaymentMethodsOutput>.Success(new PaymentMethodsOutput([new PaymentMethodOutput("PIX")])) : MethodResult<PaymentMethodsOutput>.Error(Error(code)));

        // Act
        var result = await _controller.GetOrderPaymentMethodsAsync(_methodsUseCase, "NX7K2QH9MP", CORRELATION_ID, "ntix-web", REQUESTED_AT);

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(OrderTestData.CUSTOMER_ID.ToString(), _auditableInfo!.Value.ExecutionUser);
    }

    [Fact]
    public async Task Given_NewPix_When_Paying_Should_ReturnCreatedWithThePaymentAndUseTheTokenAsExecutionUser()
    {
        // Arrange
        _paymentUseCase.ExecuteAsync(Arg.Do<CreateOrderPaymentUseCaseInput>(input => _paymentInput = input), Arg.Do<AuditableInfoValueObject>(info => _auditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CreateOrderPaymentOutput>.Success(new CreateOrderPaymentOutput(PAYMENT, Reused: false)));

        // Act
        var result = await PayAsync();

        // Assert
        var created = Assert.IsType<CreatedResult>(result);

        Assert.Same(PAYMENT, created.Value);
        Assert.Equal("/api/v1/orders/NX7K2QH9MP", created.Location);
        Assert.Equal(OrderTestData.CUSTOMER_ID.ToString(), _auditableInfo!.Value.ExecutionUser);
        Assert.Equal(OrderTestData.CUSTOMER_ID, _paymentInput!.CustomerId);
        Assert.Equal("NX7K2QH9MP", _paymentInput.Code.Value);
        Assert.Equal("PIX", _paymentInput.Method.Value.ToString());
    }

    [Fact]
    public async Task Given_ExistingPix_When_Paying_Should_ReturnOkWithTheSamePayment()
    {
        // Arrange
        _paymentUseCase.ExecuteAsync(Arg.Any<CreateOrderPaymentUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CreateOrderPaymentOutput>.Success(new CreateOrderPaymentOutput(PAYMENT, Reused: true)));

        // Act
        var result = await PayAsync();

        // Assert
        Assert.Same(PAYMENT, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(CreateOrderPaymentUseCase.CREATE_ORDER_PAYMENT_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData(CreateOrderPaymentUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, 404)]
    [InlineData(CreateOrderPaymentUseCase.ORDER_ALREADY_PAID_NOTIFICATION_CODE, 409)]
    [InlineData(CreateOrderPaymentUseCase.ORDER_EXPIRED_NOTIFICATION_CODE, 409)]
    [InlineData(CreateOrderPaymentUseCase.ORDER_PAYMENT_WINDOW_TOO_SHORT_NOTIFICATION_CODE, 409)]
    [InlineData(CreateOrderPaymentUseCase.PAYMENT_PROVIDERS_UNAVAILABLE_NOTIFICATION_CODE, 503)]
    [InlineData("PAYMENT_METHOD_INVALID", 400)]
    [InlineData("PAYMENT_METHOD_UNAVAILABLE", 400)]
    public async Task Given_PaymentRefused_When_Answering_Should_ReturnTheStatusOfTheContract(string code, int expectedStatus)
    {
        // Arrange
        _paymentUseCase.ExecuteAsync(Arg.Any<CreateOrderPaymentUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CreateOrderPaymentOutput>.Error(Error(code)));

        // Act & Assert
        Assert.Equal(expectedStatus, StatusOf(await PayAsync()));
    }
}
