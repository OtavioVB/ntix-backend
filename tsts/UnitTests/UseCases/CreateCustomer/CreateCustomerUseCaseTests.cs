using Application.UseCases.Base.Configuration;
using Application.UseCases.CreateCustomer;
using Application.UseCases.CreateCustomer.Inputs;
using Domain.ValueObjects;
using Infra.SqlServer.Base.UnitOfWork.Interfaces;
using Infra.SqlServer.Repositories.Customers.Enums;
using Infra.SqlServer.Repositories.Customers.Interfaces;
using Infra.SqlServer.Repositories.Customers.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnitTests.Support;

namespace UnitTests.UseCases.CreateCustomer;

public sealed class CreateCustomerUseCaseTests : IDisposable
{
    private const string FULL_NAME = "Maria da Silva";
    private const string BASE_SLUG = "maria-da-silva";
    private const string VERSIONED_SLUG_PATTERN = "^maria-da-silva-[a-z0-9]{5}$";
    private const string EMAIL = "maria@ntix.com";
    private const string OBFUSCATED_EMAIL = "ma***@ntix.com";
    private const string DOCUMENT = "52998224725";
    private const string OBFUSCATED_DOCUMENT = "***.982.247-**";
    private const string PASSWORD = "Senha@123";
    private const string PRIVATE_KEY = "chave-de-teste";
    private const string PHONE_NUMBER = "11987654321";

    private const string CORRELATION_ID = "b7e2f0d4-8c3a-4f5e-a1b2-c3d4e5f6a7b8";
    private const string ORIGIN_SERVICE = "customer-portal";
    private const string EXECUTION_USER = "user-123";
    private const string REQUESTED_AT = "2026-10-10T03:07:15.123Z";

    private readonly ICustomerRepository _repository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CapturingLogger<CreateCustomerUseCase> _logger = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MeasurementCollector _measurements;
    private readonly CreateCustomerUseCase _useCase;

    public void Dispose()
    {
        _measurements.Dispose();
        _meterFactory.Dispose();
    }

    private void ThenFunnelRecorded(string? outcome)
    {
        var request = Assert.Single(_measurements.Measurements, measurement => measurement.Instrument == "ntix.customers.registration.requests");
        Assert.Equal(1, request.Value);

        var outcomes = _measurements.Measurements.Where(measurement => measurement.Instrument == "ntix.customers.registration.outcomes").ToArray();

        if (outcome is null)
        {
            Assert.Empty(outcomes);
            return;
        }

        var recorded = Assert.Single(outcomes);
        Assert.Equal(1, recorded.Value);
        Assert.Equal(outcome, recorded.Outcome);
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_RecordRequestAndCreatedOutcome()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded("created");
    }

    [Fact]
    public async Task Given_InvalidData_When_Executing_Should_RecordRequestAndValidationFailedOutcome()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(email: "invalid"), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded("validation_failed");
    }

    [Fact]
    public async Task Given_FullNameWithoutLettersOrNumbers_When_Executing_Should_RecordValidationFailedOutcome()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(fullName: "!!!"), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded("validation_failed");
    }

    [Fact]
    public async Task Given_RegisteredCustomer_When_Executing_Should_RecordRequestAndAlreadyExistsOutcome()
    {
        // Arrange
        GivenCustomerAlreadyExists();

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded("already_exists");
    }

    [Fact]
    public async Task Given_SameEmailCreatedAtTheSameTime_When_Executing_Should_RecordAlreadyExistsOutcome()
    {
        // Arrange
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.EmailOrDocumentAlreadyExists);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded("already_exists");
    }

    [Fact]
    public async Task Given_ApplicationException_When_Executing_Should_RecordRequestAndErrorOutcome()
    {
        // Arrange
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        ThenFunnelRecorded("error");
    }

    [Fact]
    public async Task Given_CommitFailure_When_Executing_Should_RecordErrorOutcomeInsteadOfCreated()
    {
        // Arrange
        _unitOfWork
            .CommitAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("commit failed"));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        ThenFunnelRecorded("error");
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_RecordOnlyTheRequest()
    {
        // Arrange
        _repository
            .ExistsByEmailOrDocumentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        ThenFunnelRecorded(null);
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_RecordPasswordHashDuration()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var duration = Assert.Single(_measurements.Durations);
        Assert.Equal("ntix.customers.password.hash.duration", duration.Instrument);
        Assert.True(duration.Value > 0);
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_CreateSpansOfPasswordDefinitionWithoutPassword()
    {
        // Arrange
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CreateCustomerDiagnostics.NAME,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Single(spans, span => span.OperationName == "CreateCustomer.GeneratePasswordSalt");
        var hashSpan = Assert.Single(spans, span => span.OperationName == "CreateCustomer.HashPassword");
        Assert.Equal(PasswordHashValueObject.ITERATIONS, hashSpan.GetTagItem("ntix.password.iterations"));
        Assert.All(spans, span => Assert.DoesNotContain(span.TagObjects, tag => Equals(tag.Value, PASSWORD)));
    }

    [Fact]
    public async Task Given_WeakPassword_When_Executing_Should_NotRecordPasswordHashDuration()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(password: "senha"), BuildAuditableInfo());

        // Assert
        Assert.Empty(_measurements.Durations);
    }

    public CreateCustomerUseCaseTests()
    {
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.Created);

        var diagnostics = new CreateCustomerDiagnostics(_meterFactory);

        _measurements = new MeasurementCollector(_meterFactory);

        _useCase = new CreateCustomerUseCase(
            customerRepository: _repository,
            unitOfWork: _unitOfWork,
            passwordConfiguration: new PasswordConfiguration(PRIVATE_KEY),
            diagnostics: diagnostics,
            logger: _logger);
    }

    private static CreateCustomerUseCaseInput BuildInput(
        string fullName = FULL_NAME,
        string email = EMAIL,
        string document = DOCUMENT,
        string password = PASSWORD,
        string? phoneNumber = null)
        => CreateCustomerUseCaseInput.Create(
            fullName: fullName,
            email: email,
            document: document,
            password: password,
            phoneNumber: phoneNumber);

    private static AuditableInfoValueObject BuildAuditableInfo()
        => AuditableInfoValueObject.Build(CORRELATION_ID, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

    private void GivenCustomerAlreadyExists()
        => _repository
            .ExistsByEmailOrDocumentAsync(EMAIL, DOCUMENT, Arg.Any<CancellationToken>())
            .Returns(true);

    private Task ThenCustomerWasNotCreated()
        => _repository
            .DidNotReceive()
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>());

    private async Task ThenRolledBackWithoutCommit()
    {
        await _unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    private void ThenLogsDoNotExposePersonalData()
        => Assert.All(_logger.Logs, log =>
        {
            Assert.DoesNotContain(EMAIL, log.Message);
            Assert.DoesNotContain(DOCUMENT, log.Message);
            Assert.DoesNotContain(FULL_NAME, log.Message);
            Assert.DoesNotContain(BASE_SLUG, log.Message);
        });

    [Fact]
    public async Task Given_InvalidFields_When_Executing_Should_ReturnNotificationOfEachFieldWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(fullName: "", email: "invalid", document: "123");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Collection(
            result.Notifications,
            notification => Assert.Equal("FULL_NAME_COULD_NOT_BE_EMPTY", notification.Code),
            notification => Assert.Equal("EMAIL_FORMAT_INVALID", notification.Code),
            notification => Assert.Equal("CPF_FORMAT_INVALID", notification.Code));
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await ThenCustomerWasNotCreated();
    }

    [Fact]
    public async Task Given_WeakPassword_When_Executing_Should_ReturnNotificationWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(password: "senha@123");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("PASSWORD_WITHOUT_UPPERCASE", notification.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidPhoneNumber_When_Executing_Should_ReturnNotificationWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(phoneNumber: "987654321");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("PHONE_NUMBER_FORMAT_INVALID", notification.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_CreateWithHashOfPasswordSaltAndPrivateKey()
    {
        // Arrange
        CustomerModel? created = null;
        _repository
            .CreateAsync(Arg.Do<CustomerModel>(customer => created = customer), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.Created);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.NotNull(created);
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(PASSWORD, created.PasswordSalt, PasswordHashValueObject.ITERATIONS, HashAlgorithmName.SHA256, PasswordHashValueObject.LENGTH);
        Assert.Equal(HMACSHA256.HashData(Encoding.UTF8.GetBytes(PRIVATE_KEY), passwordHash), created.PasswordHash);
        Assert.Equal(PasswordSaltValueObject.LENGTH, created.PasswordSalt.Length);
    }

    [Fact]
    public async Task Given_PhoneNumber_When_Executing_Should_CreateWithNormalizedPhoneNumber()
    {
        // Arrange
        var input = BuildInput(phoneNumber: "(11) 98765-4321");

        // Act
        await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        await _repository
            .Received(1)
            .CreateAsync(Arg.Is<CustomerModel>(customer => customer.PhoneNumber == PHONE_NUMBER), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NoPhoneNumber_When_Executing_Should_CreateWithoutPhoneNumber()
    {
        // Arrange & Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository
            .Received(1)
            .CreateAsync(Arg.Is<CustomerModel>(customer => customer.PhoneNumber == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_SlugConflict_When_Executing_Should_KeepSamePasswordHashAndSalt()
    {
        // Arrange
        var created = new List<CustomerModel>();
        _repository
            .CreateAsync(Arg.Do<CustomerModel>(created.Add), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.SlugAlreadyExists, CustomerCreationResult.Created);

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.Equal(2, created.Count);
        Assert.Equal(created[0].PasswordHash, created[1].PasswordHash);
        Assert.Equal(created[0].PasswordSalt, created[1].PasswordSalt);
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_NotLogPasswordOrPhoneNumber()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(phoneNumber: PHONE_NUMBER), BuildAuditableInfo());

        // Assert
        Assert.All(_logger.Logs, log =>
        {
            Assert.DoesNotContain(PASSWORD, log.Message);
            Assert.DoesNotContain(PHONE_NUMBER, log.Message);
        });
    }

    [Fact]
    public async Task Given_InvalidAuditableInfo_When_Executing_Should_ReturnItsNotificationsWithoutOpeningTransaction()
    {
        // Arrange
        var auditableInfo = AuditableInfoValueObject.Build(null, ORIGIN_SERVICE, EXECUTION_USER, REQUESTED_AT);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), auditableInfo);

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("AUDITABLE_CORRELATION_ID_REQUIRED", notification.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_InvalidInputAndAuditableInfo_When_Executing_Should_ReportAllProblemsAtOnce()
    {
        // Arrange
        var auditableInfo = AuditableInfoValueObject.Build(CORRELATION_ID, null, EXECUTION_USER, REQUESTED_AT);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(email: "invalid"), auditableInfo);

        // Assert
        Assert.Collection(
            result.Notifications,
            notification => Assert.Equal("EMAIL_FORMAT_INVALID", notification.Code),
            notification => Assert.Equal("AUDITABLE_ORIGIN_SERVICE_REQUIRED", notification.Code));
    }

    [Fact]
    public async Task Given_InvalidData_When_Executing_Should_LogRejectionWithCodes()
    {
        // Arrange
        var input = BuildInput(email: "invalid");

        // Act
        await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains("EMAIL_FORMAT_INVALID", log.Message);
    }

    [Fact]
    public async Task Given_FullNameWithoutLettersOrNumbers_When_Executing_Should_ReturnNotGeneratedNotificationWithoutOpeningTransaction()
    {
        // Arrange
        var input = BuildInput(fullName: "!!!");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("SLUG_COULD_NOT_BE_GENERATED", notification.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_EmailOrDocumentAlreadyRegistered_When_Executing_Should_RollbackAndReturnAlreadyExistsNotification()
    {
        // Arrange
        GivenCustomerAlreadyExists();

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CUSTOMER_ALREADY_EXISTS", notification.Code);
        await ThenCustomerWasNotCreated();
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_EmailOrDocumentAlreadyRegistered_When_Executing_Should_LogRejectionWithObfuscatedData()
    {
        // Arrange
        GivenCustomerAlreadyExists();

        // Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        Assert.Contains(OBFUSCATED_DOCUMENT, log.Message);
        ThenLogsDoNotExposePersonalData();
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_CreateWithNormalizedDataAndCommitInOrder()
    {
        // Arrange
        var input = BuildInput(
            fullName: "  Maria   da Silva ",
            email: "MARIA@ntix.com",
            document: "529.982.247-25");

        // Act
        var result = await _useCase.ExecuteAsync(input, BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CREATE_CUSTOMER_SUCCESS", notification.Code);
        Received.InOrder(async () =>
        {
            await _unitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>());
            await _repository.ExistsByEmailOrDocumentAsync(EMAIL, DOCUMENT, Arg.Any<CancellationToken>());
            await _repository.ExistsBySlugAsync(BASE_SLUG, Arg.Any<CancellationToken>());
            await _repository.CreateAsync(
                Arg.Is<CustomerModel>(customer =>
                    customer.Id != Guid.Empty
                    && customer.FullName == FULL_NAME
                    && customer.Email == EMAIL
                    && customer.Document == DOCUMENT
                    && customer.Slug == BASE_SLUG
                    && customer.CreatedCorrelationId == Guid.Parse(CORRELATION_ID)),
                Arg.Any<CancellationToken>());
            await _unitOfWork.CommitAsync(Arg.Any<CancellationToken>());
        });
        await _unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_LogCreationWithObfuscatedData()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Information, log.Level);
        Assert.Contains(OBFUSCATED_EMAIL, log.Message);
        Assert.Contains(OBFUSCATED_DOCUMENT, log.Message);
        ThenLogsDoNotExposePersonalData();
    }

    [Fact]
    public async Task Given_ValidAuditableInfo_When_Executing_Should_OpenLogScopeWithAuditableFields()
    {
        // Arrange & Act
        await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        var scope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(_logger.Scopes));
        Assert.Equal(Guid.Parse(CORRELATION_ID), scope["CorrelationId"]);
        Assert.Equal(ORIGIN_SERVICE, scope["OriginService"]);
        Assert.Equal(EXECUTION_USER, scope["ExecutionUser"]);
        Assert.Equal(new DateTime(2026, 10, 10, 3, 7, 15, 123, DateTimeKind.Utc), scope["RequestedAt"]);
    }

    [Fact]
    public async Task Given_BaseSlugTaken_When_Executing_Should_CreateWithRandomVersionOfSlug()
    {
        // Arrange
        _repository
            .ExistsBySlugAsync(BASE_SLUG, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository
            .Received(1)
            .CreateAsync(Arg.Is<CustomerModel>(customer => Regex.IsMatch(customer.Slug, VERSIONED_SLUG_PATTERN)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_RandomVersionsTaken_When_Executing_Should_KeepGeneratingUntilOneIsFree()
    {
        // Arrange
        _repository
            .ExistsBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true, true, true, false);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository
            .Received(4)
            .ExistsBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Given_SlugTakenAtTheSameTimeRepeatedly_When_Executing_Should_RetryWithoutLimitAndCommitOnce()
    {
        // Arrange
        const int CONFLICTS = 20;
        var results = Enumerable.Repeat(CustomerCreationResult.SlugAlreadyExists, CONFLICTS - 1).Append(CustomerCreationResult.Created).ToArray();
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.SlugAlreadyExists, results);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        await _repository
            .Received(CONFLICTS + 1)
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        ThenLogsDoNotExposePersonalData();
    }

    [Fact]
    public async Task Given_SameEmailCreatedAtTheSameTime_When_Executing_Should_RollbackAndReturnAlreadyExistsNotification()
    {
        // Arrange
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .Returns(CustomerCreationResult.EmailOrDocumentAlreadyExists);

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CUSTOMER_ALREADY_EXISTS", notification.Code);
        await ThenRolledBackWithoutCommit();
    }

    [Fact]
    public async Task Given_RepositoryFailure_When_Executing_Should_RollbackLogErrorAndPropagateException()
    {
        // Arrange
        var failure = new InvalidOperationException("database unavailable");
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        Assert.Same(failure, thrown);
        await ThenRolledBackWithoutCommit();
        var log = Assert.Single(_logger.Logs);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Same(failure, log.Exception);
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
    }

    [Fact]
    public async Task Given_RollbackFailure_When_HandlingException_Should_LogBothFailuresAndPropagateOriginalException()
    {
        // Arrange
        var failure = new InvalidOperationException("database unavailable");
        var rollbackFailure = new InvalidOperationException("rollback failed");
        _repository
            .CreateAsync(Arg.Any<CustomerModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        _unitOfWork
            .RollbackAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(rollbackFailure);

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo()));

        // Assert
        Assert.Same(failure, thrown);
        Assert.Contains(_logger.Logs, log => ReferenceEquals(log.Exception, failure));
        Assert.Contains(_logger.Logs, log => ReferenceEquals(log.Exception, rollbackFailure));
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_RollbackAndReturnCanceledNotification()
    {
        // Arrange
        _repository
            .ExistsByEmailOrDocumentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _useCase.ExecuteAsync(BuildInput(), BuildAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(CreateCustomerUseCase.CREATE_CUSTOMER_CANCELED_NOTIFICATION_CODE, notification.Code);
        await ThenRolledBackWithoutCommit();
        Assert.Empty(_logger.Logs);
    }
}
