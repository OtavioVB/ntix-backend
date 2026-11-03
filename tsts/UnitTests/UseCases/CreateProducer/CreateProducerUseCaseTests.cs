using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateProducer;
using Application.UseCases.CreateProducer.Inputs;
using Domain.ValueObjects;
using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Interfaces;
using Infra.PostgreSql.Repositories.Outbox.Models;
using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string PHONE = "(11) 98765-4321";
    private const string E164_PHONE = "+5511987654321";
    private const string ADMINISTRATOR_NAME = "Maria da Silva";
    private const string PASSWORD = "Senha@123";
    private const string PRIVATE_KEY = "chave-de-teste";

    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "ntix-web";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private readonly IProducerRepository _producerRepository = Substitute.For<IProducerRepository>();
    private readonly IOutboxRepository _outboxRepository = Substitute.For<IOutboxRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CapturingLogger<CreateProducerUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CreateProducerUseCase _useCase;

    public CreateProducerUseCaseTests()
    {
        _producerRepository
            .CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerCreationResult.Created);
        _producerRepository
            .CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerUserCreationResult.Created);

        var diagnostics = new CreateProducerDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new CreateProducerUseCase(
            producerRepository: _producerRepository,
            outboxRepository: _outboxRepository,
            unitOfWork: _unitOfWork,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            diagnostics: diagnostics,
            logger: _logger);
    }

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private static CreateProducerUseCaseInput BuildInput(
        string name = NAME,
        string email = EMAIL,
        string phone = PHONE,
        string administratorName = ADMINISTRATOR_NAME,
        string password = PASSWORD)
        => CreateProducerUseCaseInput.Create(
            name: name,
            email: email,
            phone: phone,
            administratorName: administratorName,
            administratorPassword: password);

    // Cadastro público: sem usuário executor.
    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, null, REQUESTED_AT);

    private void GivenEmailAlreadyRegistered()
        => _producerRepository
            .ExistsByEmailAsync(EMAIL, Arg.Any<CancellationToken>())
            .Returns(true);

    private async Task ThenRolledBackWithoutCommit()
    {
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    private Task ThenNoEventWasRecorded()
        => _outboxRepository.DidNotReceive().AddAsync(Arg.Any<OutboxEventModel>(), Arg.Any<CancellationToken>());

    private void ThenLogsDoNotExposePersonalData()
        => Assert.All(_logger.Logs, log =>
        {
            Assert.DoesNotContain(EMAIL, log.Message);
            Assert.DoesNotContain(PASSWORD, log.Message);
            Assert.DoesNotContain(ADMINISTRATOR_NAME, log.Message);
            Assert.DoesNotContain(E164_PHONE, log.Message);
            Assert.DoesNotContain(BASE_SLUG, log.Message);
        });

    private void ThenFunnelRecorded(string? outcome)
    {
        var request = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.producers.registration.requests");
        Assert.Equal(1, request.Value);

        var outcomes = _measurements.Measurements.Where(measurement => measurement.Instrument == "ntix.producers.registration.outcomes").ToArray();

        if (outcome is null)
        {
            Assert.Empty(outcomes);
            return;
        }

        var recorded = Assert.Single(outcomes);
        Assert.Equal(outcome, recorded.Outcome);
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_CreatePendingProducerAdministratorAndEventInOneTransaction()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(email: "MARIA@ntix.com"), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("CREATE_PRODUCER_SUCCESS", Assert.Single(result.Notifications).Code);
        Received.InOrder(async () =>
        {
            await _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            await _producerRepository.ExistsByEmailAsync(EMAIL, Arg.Any<CancellationToken>());
            await _producerRepository.ExistsBySlugAsync(BASE_SLUG, Arg.Any<CancellationToken>());
            await _producerRepository.CreateAsync(
                Arg.Is<ProducerModel>(producer =>
                    producer.Name == NAME
                    && producer.Email == EMAIL
                    && producer.Phone == E164_PHONE
                    && producer.Slug == BASE_SLUG
                    && producer.Status == "PENDING"
                    && producer.CreatedCorrelationId == Guid.Parse(CORRELATION_ID)),
                Arg.Any<CancellationToken>());
            await _producerRepository.CreateUserAsync(
                Arg.Is<ProducerUserModel>(user =>
                    user.Name == ADMINISTRATOR_NAME
                    && user.Email == EMAIL
                    && user.UserType == "ADMINISTRATOR"
                    && user.Status == "PENDING"
                    && user.CreatedCorrelationId == Guid.Parse(CORRELATION_ID)),
                Arg.Any<CancellationToken>());
            await _outboxRepository.AddAsync(Arg.Is<OutboxEventModel>(outboxEvent => outboxEvent.EventType == "ProducerRegistered"), Arg.Any<CancellationToken>());
            await _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_LinkAdministratorAndEventToTheProducer()
    {
        // Arrange
        ProducerModel? producer = null;
        ProducerUserModel? administrator = null;
        OutboxEventModel? outboxEvent = null;
        _producerRepository.CreateAsync(Arg.Do<ProducerModel>(created => producer = created), Arg.Any<CancellationToken>()).Returns(ProducerCreationResult.Created);
        _producerRepository.CreateUserAsync(Arg.Do<ProducerUserModel>(created => administrator = created), Arg.Any<CancellationToken>()).Returns(ProducerUserCreationResult.Created);
        await _outboxRepository.AddAsync(Arg.Do<OutboxEventModel>(added => outboxEvent = added), Arg.Any<CancellationToken>());

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.NotNull(producer);
        Assert.NotNull(administrator);
        Assert.NotNull(outboxEvent);
        Assert.Equal(producer.Id, administrator.ProducerId);
        Assert.Equal(producer.Id, outboxEvent.AggregateId);
        using var payload = JsonDocument.Parse(outboxEvent.Payload);
        Assert.Equal(producer.Id, payload.RootElement.GetProperty("ProducerId").GetGuid());
        Assert.Equal($"producer:{producer.Id}", payload.RootElement.GetProperty("WalletIdempotencyKey").GetString());
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_StoreOnlyHashOfPasswordSaltAndPrivateKey()
    {
        // Arrange
        ProducerUserModel? administrator = null;
        _producerRepository.CreateUserAsync(Arg.Do<ProducerUserModel>(created => administrator = created), Arg.Any<CancellationToken>()).Returns(ProducerUserCreationResult.Created);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.NotNull(administrator);
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(PASSWORD, administrator.PasswordSalt, PasswordHashValueObject.ITERATIONS, HashAlgorithmName.SHA256, PasswordHashValueObject.LENGTH);
        Assert.Equal(HMACSHA256.HashData(Encoding.UTF8.GetBytes(PRIVATE_KEY), passwordHash), administrator.PasswordHash);
    }

    [Fact]
    public async Task Given_InvalidFields_When_Executing_Should_ReturnNotificationOfEachFieldWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(name: "", email: "invalid", phone: "123", administratorName: "", password: "senha");

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
    public async Task Given_WeakPassword_When_Executing_Should_FollowCustomerPasswordPolicy()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(password: "senha@123"), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("PASSWORD_WITHOUT_UPPERCASE", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NameWithoutLettersOrNumbers_When_Executing_Should_ReturnSlugNotGeneratedNotification()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(name: "!!!"), BuildAuditableInfo());

        // Assert
        Assert.Equal("SLUG_COULD_NOT_BE_GENERATED", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EmailAlreadyRegistered_When_Executing_Should_RollbackWithoutCreatingAnything()
    {
        // Arrange
        GivenEmailAlreadyRegistered();

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("PRODUCER_EMAIL_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _producerRepository.DidNotReceive().CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>());
        await ThenNoEventWasRecorded();
        await ThenRolledBackWithoutCommit();
        ThenFunnelRecorded("already_exists");
    }

    [Fact]
    public async Task Given_EmailAlreadyRegistered_When_Executing_Should_LogRejectionWithObfuscatedEmail()
    {
        // Arrange
        GivenEmailAlreadyRegistered();

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        ThenLogsDoNotExposePersonalData();
    }

    [Fact]
    public async Task Given_SameEmailRegisteredAtTheSameTime_When_CreatingProducer_Should_RollbackAndReturnAlreadyExists()
    {
        // Arrange
        _producerRepository
            .CreateAsync(Arg.Any<ProducerModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerCreationResult.EmailAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal("PRODUCER_EMAIL_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await _producerRepository.DidNotReceive().CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>());
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_AdministratorCannotBeCreated_When_Executing_Should_RollbackTheProducerToo()
    {
        // Arrange
        _producerRepository
            .CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>())
            .Returns(ProducerUserCreationResult.EmailAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("PRODUCER_EMAIL_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        await ThenNoEventWasRecorded();
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_AdministratorCreationFailure_When_Executing_Should_RollbackAndPropagateException()
    {
        // Arrange
        var failure = new InvalidOperationException("database unavailable");
        _producerRepository
            .CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        Assert.Same(failure, thrown);
        await ThenRolledBackWithoutCommit();
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Logs).Level);
        ThenFunnelRecorded("error");
    }

    [Fact]
    public async Task Given_BaseSlugTaken_When_Executing_Should_CreateWithRandomVersionOfSlug()
    {
        // Arrange
        _producerRepository
            .ExistsBySlugAsync(BASE_SLUG, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        await _producerRepository
            .Received(1)
            .CreateAsync(Arg.Is<ProducerModel>(producer => Regex.IsMatch(producer.Slug, VERSIONED_SLUG_PATTERN)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("WWW", "www")]
    [InlineData("Api", "api")]
    [InlineData("Admin", "admin")]
    [InlineData("App", "app")]
    [InlineData("Hub", "hub")]
    [InlineData("Produtores", "produtores")]
    public async Task Given_NameThatGeneratesReservedWord_When_Executing_Should_CreateWithRandomVersionOfSlug(string name, string reserved)
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(name: name), BuildAuditableInfo());

        // Assert
        await _producerRepository.DidNotReceive().ExistsBySlugAsync(reserved, Arg.Any<CancellationToken>());
        await _producerRepository
            .Received(1)
            .CreateAsync(Arg.Is<ProducerModel>(producer => Regex.IsMatch(producer.Slug, $"^{reserved}-[a-z0-9]{{5}}$")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_VeryLongName_When_Executing_Should_LimitSlugToSubdomainLength()
    {
        // Arrange
        var name = new string('a', 200);
        _producerRepository
            .ExistsBySlugAsync(Arg.Is<string>(slug => slug.Length == 63 && !slug.Contains('-')), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _useCase.ExecuteAsync(BuildInput(name: name), BuildAuditableInfo());

        // Assert
        await _producerRepository
            .Received(1)
            .CreateAsync(Arg.Is<ProducerModel>(producer => producer.Slug.Length <= SlugValueObject.SUBDOMAIN_MAX_LENGTH), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_SlugTakenAtTheSameTime_When_Executing_Should_RetryWithAnotherVersionAndCommitOnce()
    {
        // Arrange
        var created = new List<ProducerModel>();
        _producerRepository
            .CreateAsync(Arg.Do<ProducerModel>(created.Add), Arg.Any<CancellationToken>())
            .Returns(ProducerCreationResult.SlugAlreadyExists, ProducerCreationResult.SlugAlreadyExists, ProducerCreationResult.Created);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, created.Count);
        Assert.Equal(3, created.Select(producer => producer.Slug).Distinct().Count());
        await _producerRepository.Received(1).CreateUserAsync(Arg.Any<ProducerUserModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        ThenLogsDoNotExposePersonalData();
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_LogCreationWithObfuscatedEmail()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        ThenLogsDoNotExposePersonalData();
        ThenFunnelRecorded("created");
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_ReturnItsNotificationsWithoutOpeningTransaction()
    {
        // Arrange
        var auditableInfo = AuditableInfoValueObject.Build(null, ORIGIN_SERVICE, null, REQUESTED_AT);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), auditableInfo);

        // Assert
        Assert.Equal("AUDITABLE_CORRELATION_ID_REQUIRED", Assert.Single(result.Notifications).Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_CommitFailure_When_Executing_Should_RollbackAndPropagateException()
    {
        // Arrange
        _unitOfWork
            .CommitAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("commit failed"));

        // Act
        var execute = () => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(execute);
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        ThenFunnelRecorded("error");
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_RollbackAndReturnCanceledNotification()
    {
        // Arrange
        _producerRepository
            .ExistsByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal(CreateProducerUseCase.CREATE_PRODUCER_CANCELED_NOTIFICATION_CODE, Assert.Single(result.Notifications).Code);
        await ThenRolledBackWithoutCommit();
        Assert.Empty(_logger.Logs);
        ThenFunnelRecorded(null);
    }
}
