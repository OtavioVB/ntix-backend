using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetCustomerOrders;
using Application.UseCases.GetCustomerOrders.Inputs;
using Application.UseCases.GetCustomerTicket;
using Application.UseCases.GetCustomerTicket.Inputs;
using Application.UseCases.GetCustomerTickets;
using Application.UseCases.GetCustomerTickets.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using System.Security.Claims;
using UnitTests.Support;
using WebApi.Authentication;
using WebApi.Controllers;

namespace UnitTests.Controllers;

/// <summary>Tradução da área do comprador (REQ13) para o HTTP: sucesso, validação, cliente inexistente, cancelamento e ingresso não encontrado.</summary>
public sealed class CustomersV1ControllerCustomerAreaTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;

    private static readonly Guid CUSTOMER_ID = Guid.Parse("1d2c3b4a-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private readonly IUseCase<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput> _ordersUseCase = Substitute.For<IUseCase<GetCustomerOrdersUseCaseInput, CustomerOrdersPageOutput>>();
    private readonly IUseCase<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput> _ticketsUseCase = Substitute.For<IUseCase<GetCustomerTicketsUseCaseInput, CustomerTicketsPageOutput>>();
    private readonly IUseCase<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput> _ticketUseCase = Substitute.For<IUseCase<GetCustomerTicketUseCaseInput, CustomerTicketDetailOutput>>();
    private readonly CustomersV1Controller _controller;

    public CustomersV1ControllerCustomerAreaTests()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomerClaims.SUBJECT, CUSTOMER_ID.ToString()), new Claim(CustomerClaims.ACTOR, CustomerClaims.CUSTOMER_ACTOR)], "Bearer")),
        };

        _controller = new CustomersV1Controller { ControllerContext = new ControllerContext { HttpContext = httpContext } };
    }

    private static MethodResult<T> Error<T>(string code)
        => MethodResult<T>.Error([Notification.Error(code, "mensagem")]);

    private static int? StatusOf(IActionResult result)
        => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => null,
        };

    private static readonly CustomerOrdersPageOutput EMPTY_ORDERS = new([], 1, 20, 0);
    private static readonly CustomerTicketsPageOutput EMPTY_TICKETS = new([], 1, 20, 0);

    [Fact]
    public async Task Given_ServedOrders_When_ListingMyOrders_Should_RespondOk()
    {
        // Arrange
        _ordersUseCase.ExecuteAsync(Arg.Any<GetCustomerOrdersUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CustomerOrdersPageOutput>.Success(EMPTY_ORDERS));

        // Act
        var result = await _controller.GetMyOrdersAsync(_ordersUseCase, "PAID", 1, 20, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Given_InvalidFilter_When_ListingMyOrders_Should_RespondBadRequest()
    {
        // Arrange
        _ordersUseCase.ExecuteAsync(Arg.Any<GetCustomerOrdersUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerOrdersPageOutput>(CustomerListFiltersValueObject.ORDER_FILTER_INVALID_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyOrdersAsync(_ordersUseCase, "NOPE", null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(400, StatusOf(result));
    }

    [Fact]
    public async Task Given_UnknownCustomer_When_ListingMyOrders_Should_RespondNotFound()
    {
        // Arrange
        _ordersUseCase.ExecuteAsync(Arg.Any<GetCustomerOrdersUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerOrdersPageOutput>(GetCustomerOrdersUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyOrdersAsync(_ordersUseCase, null, null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(404, StatusOf(result));
    }

    [Fact]
    public async Task Given_CanceledListing_When_ListingMyOrders_Should_RespondClientClosedRequest()
    {
        // Arrange
        _ordersUseCase.ExecuteAsync(Arg.Any<GetCustomerOrdersUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerOrdersPageOutput>(GetCustomerOrdersUseCase.GET_CUSTOMER_ORDERS_CANCELED_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyOrdersAsync(_ordersUseCase, null, null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(CLIENT_CLOSED_REQUEST, StatusOf(result));
    }

    [Fact]
    public async Task Given_ServedTickets_When_ListingMyTickets_Should_RespondOk()
    {
        // Arrange
        _ticketsUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CustomerTicketsPageOutput>.Success(EMPTY_TICKETS));

        // Act
        var result = await _controller.GetMyTicketsAsync(_ticketsUseCase, "PAST", null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Given_InvalidPeriod_When_ListingMyTickets_Should_RespondBadRequest()
    {
        // Arrange
        _ticketsUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerTicketsPageOutput>(CustomerListFiltersValueObject.TICKET_FILTER_INVALID_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyTicketsAsync(_ticketsUseCase, "FUTURO", null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(400, StatusOf(result));
    }

    [Fact]
    public async Task Given_CanceledTickets_When_ListingMyTickets_Should_RespondClientClosedRequest()
    {
        // Arrange
        _ticketsUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerTicketsPageOutput>(GetCustomerTicketsUseCase.GET_CUSTOMER_TICKETS_CANCELED_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyTicketsAsync(_ticketsUseCase, null, null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(CLIENT_CLOSED_REQUEST, StatusOf(result));
    }

    [Fact]
    public async Task Given_UnknownCustomerForTickets_When_ListingMyTickets_Should_RespondNotFound()
    {
        // Arrange
        _ticketsUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketsUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerTicketsPageOutput>(GetCustomerTicketsUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyTicketsAsync(_ticketsUseCase, null, null, null, ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(404, StatusOf(result));
    }

    [Fact]
    public async Task Given_OwnTicket_When_OpeningMyTicket_Should_RespondOk()
    {
        // Arrange
        _ticketUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CustomerTicketDetailOutput>.Success(null!));

        // Act
        var result = await _controller.GetMyTicketAsync(_ticketUseCase, "K7M2QX9HP4TA", ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Given_TicketOfAnotherCustomer_When_OpeningMyTicket_Should_RespondNotFound()
    {
        // Arrange (RN1)
        _ticketUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerTicketDetailOutput>(GetCustomerTicketUseCase.TICKET_NOT_FOUND_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyTicketAsync(_ticketUseCase, "K7M2QX9HP4TA", ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(404, StatusOf(result));
    }

    [Fact]
    public async Task Given_CanceledTicketRead_When_OpeningMyTicket_Should_RespondClientClosedRequest()
    {
        // Arrange
        _ticketUseCase.ExecuteAsync(Arg.Any<GetCustomerTicketUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(Error<CustomerTicketDetailOutput>(GetCustomerTicketUseCase.GET_CUSTOMER_TICKET_CANCELED_NOTIFICATION_CODE));

        // Act
        var result = await _controller.GetMyTicketAsync(_ticketUseCase, "K7M2QX9HP4TA", ProducerSessionTestData.CORRELATION_ID, "ntix-web", ProducerSessionTestData.REQUESTED_AT);

        // Assert
        Assert.Equal(CLIENT_CLOSED_REQUEST, StatusOf(result));
    }
}
