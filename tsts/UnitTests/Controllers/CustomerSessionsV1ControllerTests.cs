using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomerSession;
using Application.UseCases.CreateCustomerSession.Inputs;
using Application.UseCases.GetCurrentCustomer;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Application.UseCases.RefreshCustomerSession;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Application.UseCases.RevokeCustomerSession;
using Application.UseCases.RevokeCustomerSession.Inputs;
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

/// <summary>Tradução do resultado dos casos de uso da sessão do cliente (REQ12) para o HTTP do contrato.</summary>
public sealed class CustomerSessionsV1ControllerTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;
    private const string CORRELATION_ID = ProducerSessionTestData.CORRELATION_ID;
    private const string REQUESTED_AT = ProducerSessionTestData.REQUESTED_AT;

    private static readonly Guid CUSTOMER_ID = Guid.NewGuid();
    private static readonly CustomerSessionOutput SESSION = new("access", 900, "refresh", 2592000, new CustomerSessionCustomerOutput(CUSTOMER_ID, "Marina"));

    private readonly IUseCase<CreateCustomerSessionUseCaseInput, CustomerSessionOutput> _createUseCase = Substitute.For<IUseCase<CreateCustomerSessionUseCaseInput, CustomerSessionOutput>>();
    private readonly IUseCase<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput> _refreshUseCase = Substitute.For<IUseCase<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput>>();
    private readonly IUseCase<RevokeCustomerSessionUseCaseInput> _revokeUseCase = Substitute.For<IUseCase<RevokeCustomerSessionUseCaseInput>>();
    private readonly IUseCase<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput> _meUseCase = Substitute.For<IUseCase<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput>>();
    private readonly LoginAttemptLimiter _limiter = new(new LoginAttemptConfiguration(PermitLimit: 1, WindowMinutes: 15), new FakeTimeProvider(ProducerSessionTestData.NOW));
    private readonly CustomerSessionsV1Controller _controller;

    private AuditableInfoValueObject? _meAuditableInfo;

    public CustomerSessionsV1ControllerTests()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(CustomerClaims.SUBJECT, CUSTOMER_ID.ToString()), new Claim(CustomerClaims.ACTOR, CustomerClaims.CUSTOMER_ACTOR)], "Bearer")),
        };

        _controller = new CustomerSessionsV1Controller { ControllerContext = new ControllerContext { HttpContext = httpContext } };
    }

    private static INotification[] Error(string code)
        => [Notification.Error(code, "mensagem")];

    private Task<IActionResult> LoginAsync(string login = CustomerSessionTestData.EMAIL)
        => _controller.CreateCustomerSessionAsync(_createUseCase, _limiter, CORRELATION_ID, "ntix-web", null, REQUESTED_AT, new CreateCustomerSessionV1Request(login, "Senha@123"));

    private static int? StatusOf(IActionResult result)
        => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => null,
        };

    [Fact]
    public async Task Given_SessionCreated_When_LoggingIn_Should_ReturnOkWithTheSession()
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Any<CreateCustomerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>()).Returns(MethodResult<CustomerSessionOutput>.Success(SESSION));

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Same(SESSION, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(CreateCustomerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, 401)]
    [InlineData(CreateCustomerSessionUseCase.CREATE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("LOGIN_REQUIRED", 400)]
    public async Task Given_LoginRefused_When_Answering_Should_ReturnTheStatusOfTheContract(string code, int expectedStatus)
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Any<CreateCustomerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>()).Returns(MethodResult<CustomerSessionOutput>.Error(Error(code)));

        // Act & Assert
        Assert.Equal(expectedStatus, StatusOf(await LoginAsync()));
    }

    [Fact]
    public async Task Given_RateLimited_When_LoggingIn_Should_Return429WithRetryAfterForTheSameLogin()
    {
        // Arrange
        Assert.True(_limiter.TryAcquire(CustomerLoginValueObject.Build(CustomerSessionTestData.MASKED_CPF).AttemptKey));
        Assert.False(_limiter.TryAcquire(CustomerLoginValueObject.Build(CustomerSessionTestData.CPF).AttemptKey));
        _createUseCase.ExecuteAsync(Arg.Any<CreateCustomerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<CustomerSessionOutput>.Error(Error(CreateCustomerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE)));

        // Act
        var result = await LoginAsync(CustomerSessionTestData.MASKED_CPF);

        // Assert
        Assert.Equal(StatusCodes.Status429TooManyRequests, StatusOf(result));
        Assert.Equal("900", _controller.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(RefreshCustomerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, 401)]
    [InlineData(RefreshCustomerSessionUseCase.REFRESH_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", 400)]
    public async Task Given_RefreshResult_When_Answering_Should_ReturnTheStatusOfTheContract(string? code, int expectedStatus)
    {
        // Arrange
        _refreshUseCase.ExecuteAsync(Arg.Any<RefreshCustomerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(code is null ? MethodResult<CustomerSessionOutput>.Success(SESSION) : MethodResult<CustomerSessionOutput>.Error(Error(code)));

        // Act
        var result = await _controller.RefreshCustomerSessionAsync(_refreshUseCase, CORRELATION_ID, "ntix-web", null, REQUESTED_AT, new CustomerRefreshTokenV1Request("refresh"));

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
    }

    [Theory]
    [InlineData(null, 204)]
    [InlineData(RevokeCustomerSessionUseCase.REVOKE_CUSTOMER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", 400)]
    public async Task Given_LogoutResult_When_Answering_Should_ReturnTheStatusOfTheContract(string? code, int expectedStatus)
    {
        // Arrange
        _revokeUseCase.ExecuteAsync(Arg.Any<RevokeCustomerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(code is null ? MethodResult.Success() : MethodResult.Error(Error(code)));

        // Act
        var result = await _controller.RevokeCustomerSessionAsync(_revokeUseCase, CORRELATION_ID, "ntix-web", null, REQUESTED_AT, new CustomerRefreshTokenV1Request("refresh"));

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(GetCurrentCustomerUseCase.CUSTOMER_NOT_FOUND_NOTIFICATION_CODE, 404)]
    [InlineData(GetCurrentCustomerUseCase.GET_CURRENT_CUSTOMER_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", 400)]
    public async Task Given_MeResult_When_Answering_Should_ReturnTheStatusAndUseTheTokenAsExecutionUser(string? code, int expectedStatus)
    {
        // Arrange
        var me = new CurrentCustomerOutput(CUSTOMER_ID, "Marina Souza", CustomerSessionTestData.EMAIL, "***.982.247-**", null);
        _meUseCase.ExecuteAsync(Arg.Any<GetCurrentCustomerUseCaseInput>(), Arg.Do<AuditableInfoValueObject>(info => _meAuditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(code is null ? MethodResult<CurrentCustomerOutput>.Success(me) : MethodResult<CurrentCustomerOutput>.Error(Error(code)));

        // Act
        var result = await _controller.GetCurrentCustomerAsync(_meUseCase, CORRELATION_ID, "ntix-web", REQUESTED_AT);

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(CUSTOMER_ID.ToString(), _meAuditableInfo!.Value.ExecutionUser);
    }
}
