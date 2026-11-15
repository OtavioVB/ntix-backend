using Application.UseCases.Base.Authentication;
using Application.UseCases.Base.Configuration;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Producers.Outputs;
using Application.UseCases.CreateProducerSession;
using Application.UseCases.CreateProducerSession.Inputs;
using Application.UseCases.RefreshProducerSession;
using Application.UseCases.RefreshProducerSession.Inputs;
using Application.UseCases.RevokeProducerSession;
using Application.UseCases.RevokeProducerSession.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using UnitTests.Support;
using WebApi.Controllers;
using WebApi.Controllers.Payloads;
using static UnitTests.Support.ProducerSessionTestData;

namespace UnitTests.Controllers;

/// <summary>Tradução do resultado dos casos de uso da sessão (REQ5) para o HTTP do contrato.</summary>
public sealed class ProducerSessionsV1ControllerTests
{
    private const int CLIENT_CLOSED_REQUEST = 499;

    private static readonly ProducerSessionOutput OUTPUT = new(
        AccessToken: "access",
        AccessTokenExpiresIn: 900,
        RefreshToken: "refresh",
        RefreshTokenExpiresIn: 604800,
        User: new ProducerSessionUserOutput(Guid.NewGuid(), USER_NAME, "ADMINISTRATOR", "PENDING"),
        Producer: new ProducerSessionProducerOutput(PRODUCER_ID, PRODUCER_SLUG, "Produtora X", "PENDING"));

    private readonly IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput> _createUseCase = Substitute.For<IUseCase<CreateProducerSessionUseCaseInput, ProducerSessionOutput>>();
    private readonly IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput> _refreshUseCase = Substitute.For<IUseCase<RefreshProducerSessionUseCaseInput, ProducerSessionOutput>>();
    private readonly IUseCase<RevokeProducerSessionUseCaseInput> _revokeUseCase = Substitute.For<IUseCase<RevokeProducerSessionUseCaseInput>>();
    private readonly FakeTimeProvider _timeProvider = new(NOW);
    private readonly LoginAttemptLimiter _limiter;
    private readonly ProducerSessionsV1Controller _controller = new() { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private AuditableInfoValueObject? _receivedAuditableInfo;

    public ProducerSessionsV1ControllerTests()
    {
        _limiter = new LoginAttemptLimiter(new LoginAttemptConfiguration(PermitLimit: 1, WindowMinutes: 15), _timeProvider);
    }

    private static INotification[] Error(string code)
        => [Notification.Error(code, "mensagem")];

    private void GivenLoginResult(MethodResult<ProducerSessionOutput> result)
        => _createUseCase
            .ExecuteAsync(Arg.Any<CreateProducerSessionUseCaseInput>(), Arg.Do<AuditableInfoValueObject>(info => _receivedAuditableInfo = info), Arg.Any<CancellationToken>())
            .Returns(result);

    private Task<IActionResult> LoginAsync()
        => _controller.CreateProducerSessionAsync(_createUseCase, _limiter, CORRELATION_ID, ORIGIN_SERVICE, "quem-chamou", REQUESTED_AT, new CreateProducerSessionV1Request(EMAIL, PASSWORD));

    private Task<IActionResult> RefreshAsync()
        => _controller.RefreshProducerSessionAsync(_refreshUseCase, CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT, new RefreshProducerSessionV1Request("refresh"));

    private Task<IActionResult> RevokeAsync()
        => _controller.RevokeProducerSessionAsync(_revokeUseCase, CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT, new RevokeProducerSessionV1Request("refresh"));

    private static int? StatusOf(IActionResult result)
        => result switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => null,
        };

    [Fact]
    public async Task Given_AcceptedLogin_When_Answering_Should_Return200WithTheSessionAndPassTheHeaders()
    {
        // Arrange
        GivenLoginResult(MethodResult<ProducerSessionOutput>.Success(OUTPUT));

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Same(OUTPUT, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Guid.Parse(CORRELATION_ID), _receivedAuditableInfo!.Value.CorrelationId);
        Assert.Equal("quem-chamou", _receivedAuditableInfo.Value.ExecutionUser);
        await _createUseCase.Received(1).ExecuteAsync(
            Arg.Is<CreateProducerSessionUseCaseInput>(input => input.Email.Value == EMAIL && input.Password.Value == PASSWORD),
            Arg.Any<AuditableInfoValueObject>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CreateProducerSessionUseCase.INVALID_CREDENTIALS_NOTIFICATION_CODE, StatusCodes.Status401Unauthorized)]
    [InlineData(CreateProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, StatusCodes.Status403Forbidden)]
    [InlineData(CreateProducerSessionUseCase.CREATE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("EMAIL_FORMAT_INVALID", StatusCodes.Status400BadRequest)]
    public async Task Given_RejectedLogin_When_Answering_Should_ReturnTheStatusOfTheContractWithTheNotifications(string code, int expectedStatus)
    {
        // Arrange
        GivenLoginResult(MethodResult<ProducerSessionOutput>.Error(Error(code)));

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(code, Assert.Single(Assert.IsType<INotification[]>(Assert.IsAssignableFrom<ObjectResult>(result).Value)).Code);
        Assert.False(_controller.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Given_LoginOverTheLimit_When_Answering_Should_Return429WithSecondsUntilTheWindowEnds()
    {
        // Arrange
        var email = EmailValueObject.Build(EMAIL);
        _limiter.TryAcquire(email);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        GivenLoginResult(MethodResult<ProducerSessionOutput>.Error(Error(CreateProducerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE)));

        // Act
        var result = await LoginAsync();

        // Assert
        Assert.Equal(StatusCodes.Status429TooManyRequests, StatusOf(result));
        Assert.Equal("600", _controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Given_LoginOverTheLimitWithTheWindowJustClosed_When_Answering_Should_AskToWaitAtLeastOneSecond()
    {
        // Arrange: o limitador já reabriu a janela entre a recusa e a resposta.
        GivenLoginResult(MethodResult<ProducerSessionOutput>.Error(Error(CreateProducerSessionUseCase.TOO_MANY_LOGIN_ATTEMPTS_NOTIFICATION_CODE)));

        // Act
        await LoginAsync();

        // Assert
        Assert.Equal("1", _controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task Given_AcceptedRefresh_When_Answering_Should_Return200WithTheNewSession()
    {
        // Arrange
        _refreshUseCase.ExecuteAsync(Arg.Any<RefreshProducerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<ProducerSessionOutput>.Success(OUTPUT));

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Same(OUTPUT, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Theory]
    [InlineData(RefreshProducerSessionUseCase.INVALID_REFRESH_TOKEN_NOTIFICATION_CODE, StatusCodes.Status401Unauthorized)]
    [InlineData(RefreshProducerSessionUseCase.PRODUCER_USER_BLOCKED_NOTIFICATION_CODE, StatusCodes.Status403Forbidden)]
    [InlineData(RefreshProducerSessionUseCase.REFRESH_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", StatusCodes.Status400BadRequest)]
    public async Task Given_RejectedRefresh_When_Answering_Should_ReturnTheStatusOfTheContract(string code, int expectedStatus)
    {
        // Arrange
        _refreshUseCase.ExecuteAsync(Arg.Any<RefreshProducerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(MethodResult<ProducerSessionOutput>.Error(Error(code)));

        // Act
        var result = await RefreshAsync();

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
    }

    [Theory]
    [InlineData(null, StatusCodes.Status204NoContent)]
    [InlineData(RevokeProducerSessionUseCase.REVOKE_PRODUCER_SESSION_CANCELED_NOTIFICATION_CODE, CLIENT_CLOSED_REQUEST)]
    [InlineData("CORRELATION_ID_COULD_NOT_BE_EMPTY", StatusCodes.Status400BadRequest)]
    public async Task Given_LogoutResult_When_Answering_Should_ReturnTheStatusOfTheContract(string? errorCode, int expectedStatus)
    {
        // Arrange
        _revokeUseCase.ExecuteAsync(Arg.Any<RevokeProducerSessionUseCaseInput>(), Arg.Any<AuditableInfoValueObject>(), Arg.Any<CancellationToken>())
            .Returns(errorCode is null ? MethodResult.Success([Notification.Success("REVOKE_PRODUCER_SESSION_SUCCESS", "ok")]) : MethodResult.Error(Error(errorCode)));

        // Act
        var result = await RevokeAsync();

        // Assert
        Assert.Equal(expectedStatus, StatusOf(result));
    }
}
