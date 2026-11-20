using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Orders.Outputs;
using Application.UseCases.CreateOrder;
using Application.UseCases.CreateOrder.Inputs;
using Application.UseCases.GetCustomerOrder;
using Application.UseCases.GetCustomerOrder.Inputs;
using Domain.BoundedContexts.OrderContext.Entities;
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

/// <summary>Tradução do resultado dos casos de uso de pedidos (REQ6) para o HTTP do contrato.</summary>
public sealed class OrdersV1ControllerTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;
    private const string CORRELATION_ID = ProducerSessionTestData.CORRELATION_ID;
    private const string REQUESTED_AT = ProducerSessionTestData.REQUESTED_AT;

    private static readonly OrderOutput ORDER = new(
        "NX7K2QH9MP", "PENDING_PAYMENT", "HUB", OrderTestData.NOW, OrderTestData.NOW.AddMinutes(15), 24000,
        new OrderEventOutput("festival-horizonte", "Festival Horizonte", OrderTestData.NOW, "Parque", "Recife", "PE", null), [], []);

    private readonly IUseCase<CreateOrderUseCaseInput, OrderOutput> _createUseCase = Substitute.For<IUseCase<CreateOrderUseCaseInput, OrderOutput>>();
    private readonly IUseCase<GetCustomerOrderUseCaseInput, OrderOutput> _getUseCase = Substitute.For<IUseCase<GetCustomerOrderUseCaseInput, OrderOutput>>();
    private readonly OrdersV1Controller _controller;

    private AuditableInfoValueObject? _auditableInfo;
    private CreateOrderUseCaseInput? _createInput;

    public OrdersV1ControllerTests()
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

    private Task<IActionResult> CreateAsync(CreateOrderV1Request? request = null)
        => _controller.CreateOrderAsync(_createUseCase, CORRELATION_ID, "ntix-web", REQUESTED_AT, request ?? new CreateOrderV1Request("festival-horizonte", "HUB", [new CreateOrderItemV1Request(Guid.NewGuid().ToString(), 2)]));

    [Fact]
    public async Task Given_OrderCreated_When_Answering_Should_ReturnCreatedWithLocationAndUseTheTokenAsExecutionUser()
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Do<CreateOrderUseCaseInput>(input => _createInput = input), Arg.Do<AuditableInfoValueObject>(info => _auditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(MethodResult<OrderOutput>.Success(ORDER));

        // Act
        var result = await CreateAsync();

        // Assert
        var created = Assert.IsType<CreatedResult>(result);

        Assert.Equal("/api/v1/orders/NX7K2QH9MP", created.Location);
        Assert.Same(ORDER, created.Value);
        Assert.Equal(OrderTestData.CUSTOMER_ID.ToString(), _auditableInfo!.Value.ExecutionUser);
        Assert.Equal(OrderTestData.CUSTOMER_ID, _createInput!.CustomerId);
        Assert.Equal("festival-horizonte", _createInput.EventSlug);
        Assert.Equal(2, Assert.Single(_createInput.Items).Quantity.Value);
    }

    [Fact]
    public async Task Given_RequestWithoutItems_When_Creating_Should_PassAnEmptyListToTheUseCase()
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Do<CreateOrderUseCaseInput>(input => _createInput = input), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<OrderOutput>.Error(Error(CreateOrderUseCaseInput.ORDER_ITEMS_REQUIRED_NOTIFICATION_CODE)));

        // Act
        var result = await CreateAsync(new CreateOrderV1Request(null, null, null));

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(_createInput!.Items);
    }

    [Theory]
    [InlineData(CreateOrderUseCase.CREATE_ORDER_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData(CreateOrderUseCase.EVENT_NOT_FOUND_NOTIFICATION_CODE, 404)]
    [InlineData(Order.BATCH_INSUFFICIENT_AVAILABILITY_NOTIFICATION_CODE, 409)]
    [InlineData(Order.BATCH_NOT_ON_SALE_NOTIFICATION_CODE, 400)]
    [InlineData(Order.BATCH_NOT_FOUND_NOTIFICATION_CODE, 400)]
    [InlineData(Order.ORDER_TICKET_LIMIT_EXCEEDED_NOTIFICATION_CODE, 400)]
    [InlineData(CreateOrderUseCaseInput.ORDER_ITEM_DUPLICATED_NOTIFICATION_CODE, 400)]
    public async Task Given_OrderRefused_When_Answering_Should_ReturnTheStatusOfTheContract(string code, int expectedStatus)
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Any<CreateOrderUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<OrderOutput>.Error(Error(code)));

        // Act & Assert
        Assert.Equal(expectedStatus, StatusOf(await CreateAsync()));
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(GetCustomerOrderUseCase.ORDER_NOT_FOUND_NOTIFICATION_CODE, 404)]
    [InlineData(GetCustomerOrderUseCase.GET_CUSTOMER_ORDER_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", 400)]
    public async Task Given_GetResult_When_Answering_Should_ReturnTheStatusAndUseTheTokenAsExecutionUser(string? code, int expectedStatus)
    {
        // Arrange
        _getUseCase.ExecuteAsync(Arg.Any<GetCustomerOrderUseCaseInput>(), Arg.Do<AuditableInfoValueObject>(info => _auditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(code is null ? MethodResult<OrderOutput>.Success(ORDER) : MethodResult<OrderOutput>.Error(Error(code)));

        // Act
        var result = await _controller.GetCustomerOrderAsync(_getUseCase, "NX7K2QH9MP", CORRELATION_ID, "ntix-web", REQUESTED_AT);

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(OrderTestData.CUSTOMER_ID.ToString(), _auditableInfo!.Value.ExecutionUser);
    }
}
