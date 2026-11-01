using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Domain.ValueObjects;
using Infra.Resend.Base.EmailSender.Interfaces;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Infra.SqlServer.Repositories.Producers.Enums;
using Infra.SqlServer.Repositories.Producers.Interfaces;
using Infra.SqlServer.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnitTests.Support;

namespace UnitTests.UseCases.CreateProducer;

public sealed class CreateProducerUseCaseTests : IDisposable
{
    private const string NAME = "Ação Eventos & Shows";
    private const string BASE_SLUG = "acao-eventos-shows";
    private const string VERSIONED_SLUG_PATTERN = "^acao-eventos-shows-[a-z0-9]{5}$";
    private const string EMAIL = "maria@ntix.com";
    private const string OBFUSCATED_EMAIL = "ma***@ntix.com";
    private const string PHONE = "+5511999999999";
    private const string USER_NAME = "Maria da Silva";
    private const string PASSWORD = "Senha@123";
    private const string PRIVATE_KEY = "chave-de-teste";
    private const string LINK_BASE_URL = "https://painel.ntix.com/verificar-email";

    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "producer-portal";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private readonly IProducerRepository _repository = Substitute.For<IProducerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly CapturingLogger<CreateProducerUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CreateProducerUseCase _useCase;

    public CreateProducerUseCaseTests()
    {
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>()).Returns(ProducerCreationResult.Created);
        _repository.CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>()).Returns(ProducerUserCreationResult.Created);

        var diagnostics = new CreateProducerDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new CreateProducerUseCase(
            producerRepository: _repository,
            unitOfWork: _unitOfWork,
            emailSender: _emailSender,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            emailVerificationConfiguration: new EmailVerificationConfiguration(LINK_BASE_URL),
            diagnostics: diagnostics,
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static CreateProducerUseCaseInput BuildInput(string name = NAME, string email = EMAIL, string phone = PHONE, string userName = USER_NAME, string password = PASSWORD, string? document = null)
        => CreateProducerUseCaseInput.Create(name: name, email: email, phone: phone, document: document, userName: userName, password: password);

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT);

    private T? GetReceivedArgument<T>(string methodName) where T : class
        => _repository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == methodName)
            .Select(call => call.GetArguments()[0] as T)
            .LastOrDefault();

    private string GetSentHtml()
        => (string)_emailSender.ReceivedCalls().Single().GetArguments()[2]!;

    private void ThenFunnelRecorded(string outcome)
    {
        var request = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.registration.requests");
        Assert.Equal(1, request.Value);

        var recorded = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.registration.outcomes");
        Assert.Equal(outcome, recorded.Outcome);
    }

    private async Task ThenRolledBackWithoutCommit()
    {
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void ThenLogsDoNotExposeSensitiveData()
        => Assert.All(_logger.Logs, log =>
        {
            Assert.DoesNotContain(EMAIL, log.Message);
            Assert.DoesNotContain(PHONE, log.Message);
            Assert.DoesNotContain(USER_NAME, log.Message);
            Assert.DoesNotContain(BASE_SLUG, log.Message);
            Assert.DoesNotContain(PASSWORD, log.Message);
        });

    [Fact]
    public async Task Given_ValidInput_When_Executing_Should_CreatePendingProducerWithSlugAndCommit()
    {
        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("CREATE_PRODUCER_SUCCESS", Assert.Single(result.Notifications).Code);
        var producer = GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync));
        Assert.NotNull(producer);
        Assert.Equal("PENDING", producer.Status);
        Assert.Equal(BASE_SLUG, producer.Slug);
        Assert.Equal(EMAIL, producer.Email);
        Assert.Equal(PHONE, producer.Phone);
        Assert.Equal(Guid.Parse(CORRELATION_ID), producer.CreatedCorrelationId);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        ThenFunnelRecorded("created");
    }

    [Fact]
    public async Task Given_ValidInput_When_Executing_Should_CreatePendingAdministratorLinkedToProducer()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var producer = GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!;
        var user = GetReceivedArgument<ProducerUserModel>(nameof(IProducerRepository.CreateUserAsync));
        Assert.NotNull(user);
        Assert.Equal(producer.Id, user.ProducerId);
        Assert.Equal("ADMINISTRATOR", user.UserType);
        Assert.Equal("PENDING", user.Status);
        Assert.Equal(EMAIL, user.Email);
        Assert.Equal(USER_NAME, user.Name);
        Assert.DoesNotContain(PASSWORD, Encoding.UTF8.GetString(user.PasswordHash));
    }

    [Fact]
    public async Task Given_ValidInput_When_Executing_Should_SendLinkWhoseTokenMatchesStoredHash()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        await _emailSender.Received(1).SendAsync(EMAIL, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var match = Regex.Match(GetSentHtml(), $"{Regex.Escape(LINK_BASE_URL)}\\?token=(?<token>[a-f0-9]{{64}})");
        Assert.True(match.Success);
        var verification = GetReceivedArgument<EmailVerificationModel>(nameof(IProducerRepository.CreateEmailVerificationAsync))!;
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["token"].Value)), verification.TokenHash);
        Assert.Equal(TimeSpan.FromHours(24), verification.ExpiresAt - verification.CreatedAt);
    }

    [Fact]
    public async Task Given_ValidInput_When_Executing_Should_CommitBeforeSendingEmail()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Received.InOrder(() =>
        {
            _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
            _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Given_UserNameWithHtml_When_Executing_Should_EncodeNameInEmail()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(userName: "<b>Maria</b>"), BuildAuditableInfo());

        // Assert
        Assert.DoesNotContain("<b>Maria</b>", GetSentHtml());
    }

    [Fact]
    public async Task Given_EmailSendingFailure_When_Executing_Should_KeepRegistrationAndRecordFailure()
    {
        // Arrange
        _emailSender
            .SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("falha"));

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        Assert.Contains(_logger.Logs, log => log.EventId == 3008 && log.Level == LogLevel.Error);
        Assert.Contains(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.registration.verification_emails" && measurement.Outcome == "failed" && measurement.Value == 1);
        ThenFunnelRecorded("created");
    }

    [Fact]
    public async Task Given_InvalidFields_When_Executing_Should_ReturnNotificationOfEachFieldWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(name: "", email: "invalid", phone: "11999999999", userName: "", password: "senha");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "PRODUCER_NAME_COULD_NOT_BE_EMPTY");
        Assert.Contains(result.Notifications, notification => notification.Code == "EMAIL_FORMAT_INVALID");
        Assert.Contains(result.Notifications, notification => notification.Code == "PHONE_FORMAT_INVALID");
        Assert.Contains(result.Notifications, notification => notification.Code == "FULL_NAME_COULD_NOT_BE_EMPTY");
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenFunnelRecorded("validation_failed");
    }

    [Fact]
    public async Task Given_NameWithoutLettersOrNumbers_When_Executing_Should_ReturnSlugNotification()
    {
        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(name: "!!!"), BuildAuditableInfo());

        // Assert
        Assert.Equal("SLUG_COULD_NOT_BE_GENERATED", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        ThenFunnelRecorded("validation_failed");
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_ReturnErrorWithoutOpeningTransaction()
    {
        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), AuditableInfoValueObject.Build(null, ORIGIN_SERVICE, null, REQUESTED_AT));

        // Assert
        Assert.True(result.IsError);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RegisteredEmail_When_Executing_Should_RollbackWithoutCreating()
    {
        // Arrange
        _repository.ExistsByEmailAsync(EMAIL, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _repository.DidNotReceive().CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>());
        await ThenRolledBackWithoutCommit();
        Assert.Contains(_logger.Logs, log => log.EventId == 3002 && log.Message.Contains(OBFUSCATED_EMAIL));
        ThenLogsDoNotExposeSensitiveData();
        ThenFunnelRecorded("already_exists");
    }

    [Fact]
    public async Task Given_ConcurrentProducerWithSameEmail_When_Executing_Should_RollbackWithoutCreatingUser()
    {
        // Arrange
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>()).Returns(ProducerCreationResult.EmailAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _repository.DidNotReceive().CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>());
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_UserCreationRefused_When_Executing_Should_RollbackProducer()
    {
        // Arrange
        _repository.CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>()).Returns(ProducerUserCreationResult.EmailAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_NoDocument_When_Executing_Should_CreateProducerWithoutCheckingDocument()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Null(GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!.Document);
        await _repository.DidNotReceive().ExistsByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_Document_When_Executing_Should_PersistNormalizedDocument()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(document: "11.222.333/0001-81"), BuildAuditableInfo());

        // Assert
        Assert.Equal("11222333000181", GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!.Document);
    }

    [Fact]
    public async Task Given_InvalidDocument_When_Executing_Should_ReturnNotificationWithoutOpeningTransaction()
    {
        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(document: "123"), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_DOCUMENT_INVALID", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RegisteredDocument_When_Executing_Should_RollbackWithoutCreating()
    {
        // Arrange
        _repository.ExistsByDocumentAsync("11222333000181", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(document: "11222333000181"), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_DOCUMENT_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _repository.DidNotReceive().CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>());
        await ThenRolledBackWithoutCommit();
        Assert.All(_logger.Logs, log => Assert.DoesNotContain("11222333000181", log.Message));
        ThenFunnelRecorded("already_exists");
    }

    [Fact]
    public async Task Given_ConcurrentProducerWithSameDocument_When_Executing_Should_RollbackWithoutCreatingUser()
    {
        // Arrange
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>()).Returns(ProducerCreationResult.DocumentAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(document: "11222333000181"), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_DOCUMENT_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _repository.DidNotReceive().CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>());
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_ExistingSlug_When_Executing_Should_UseRandomVersion()
    {
        // Arrange
        _repository.ExistsBySlugAsync(BASE_SLUG, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var producer = GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!;
        Assert.Matches(VERSIONED_SLUG_PATTERN, producer.Slug);
    }

    [Fact]
    public async Task Given_ConcurrentSlugCollision_When_Executing_Should_RetryWithRandomVersion()
    {
        // Arrange
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerCreationResult.SlugAlreadyExists, ProducerCreationResult.Created);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository.Received(2).CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>());
        Assert.Matches(VERSIONED_SLUG_PATTERN, GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!.Slug);
    }

    [Fact]
    public async Task Given_ReservedName_When_Executing_Should_PersistSlugWithVersion()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(name: "Admin"), BuildAuditableInfo());

        // Assert
        Assert.Matches("^admin-[a-z0-9]{5}$", GetReceivedArgument<ProducerModel>(nameof(IProducerRepository.CreateAsync))!.Slug);
    }

    [Fact]
    public async Task Given_RepositoryFailure_When_Executing_Should_RollbackLogAndRethrow()
    {
        // Arrange
        _repository.CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));
        await ThenRolledBackWithoutCommit();
        Assert.Contains(_logger.Logs, log => log.EventId == 3005);
        ThenFunnelRecorded("error");
    }

    [Fact]
    public async Task Given_RollbackFailure_When_Executing_Should_KeepOriginalException()
    {
        // Arrange
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("falha"));
        _unitOfWork.RollbackAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));
        Assert.Contains(_logger.Logs, log => log.EventId == 3006);
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_RollbackAndReturnCanceledNotification()
    {
        // Arrange
        _repository.CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal(CreateProducerUseCase.CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_ValidInput_When_Executing_Should_LogWithoutExposingSensitiveData()
    {
        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Contains(_logger.Logs, log => log.EventId == 3001 && log.Message.Contains(OBFUSCATED_EMAIL));
        Assert.Contains(_logger.Logs, log => log.EventId == 3007);
        ThenLogsDoNotExposeSensitiveData();
        var token = Regex.Match(GetSentHtml(), "token=(?<token>[a-f0-9]{64})").Groups["token"].Value;
        Assert.All(_logger.Logs, log => Assert.DoesNotContain(token, log.Message));
    }
}
