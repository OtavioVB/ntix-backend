using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

namespace IntegratedTests.UseCases;

[Collection(PostgreSqlCollection.NAME)]
public sealed class CreateCustomerUseCaseTests
{
    private readonly PostgreSqlFixture _fixture;

    public CreateCustomerUseCaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MethodResult> ExecuteUseCaseAsync(string fullName, string email, string document, Guid? correlationId = null, string password = CustomerTestData.PASSWORD, string? phoneNumber = null)
    {
        using var scope = _fixture.Services.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>();

        return await useCase.ExecuteAsync(
            input: CreateCustomerUseCaseInput.Create(
                fullName: fullName,
                email: email,
                document: document,
                password: password,
                phoneNumber: phoneNumber),
            auditableInfo: CustomerTestData.NewAuditableInfo(correlationId));
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_ReturnCanceledNotificationAndNotPersistCustomer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        using var scope = _fixture.Services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>();

        // Act
        var result = await useCase.ExecuteAsync(
            input: CreateCustomerUseCaseInput.Create(
                fullName: CustomerTestData.NewFullName(),
                email: email,
                document: CustomerTestData.NewCpf(),
                password: CustomerTestData.PASSWORD,
                phoneNumber: null as string),
            auditableInfo: CustomerTestData.NewAuditableInfo(),
            cancellationToken: new CancellationToken(canceled: true));

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CREATE_CUSTOMER_CANCELED", notification.Code);
        Assert.Null(await _fixture.FindCustomerByEmailAsync(email));
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_PersistHashOfPasswordSaltAndPrivateKey()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf());

        // Assert
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.NotNull(customer);
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(CustomerTestData.PASSWORD, customer.PasswordSalt, PasswordHashValueObject.ITERATIONS, HashAlgorithmName.SHA256, PasswordHashValueObject.LENGTH);
        Assert.Equal(HMACSHA256.HashData(Encoding.UTF8.GetBytes(CustomerTestData.PRIVATE_KEY), passwordHash), customer.PasswordHash);
    }

    [Fact]
    public async Task Given_CustomersWithSamePassword_When_Executing_Should_PersistDifferentSaltsAndHashes()
    {
        // Arrange
        var firstEmail = CustomerTestData.NewEmail();
        var secondEmail = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), firstEmail, CustomerTestData.NewCpf());
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), secondEmail, CustomerTestData.NewCpf());

        // Assert
        var first = await _fixture.FindCustomerByEmailAsync(firstEmail);
        var second = await _fixture.FindCustomerByEmailAsync(secondEmail);
        Assert.NotEqual(first!.PasswordSalt, second!.PasswordSalt);
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
    }

    [Fact]
    public async Task Given_WeakPassword_When_Executing_Should_ReturnErrorAndNotPersistCustomer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        var result = await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), password: "senha");

        // Assert
        Assert.True(result.IsError);
        Assert.Null(await _fixture.FindCustomerByEmailAsync(email));
    }

    [Fact]
    public async Task Given_PhoneNumber_When_Executing_Should_PersistOnlyDigits()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), phoneNumber: "(11) 98765-4321");

        // Assert
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.Equal("11987654321", customer?.PhoneNumber);
    }

    [Fact]
    public async Task Given_NoPhoneNumber_When_Executing_Should_PersistCustomerWithoutPhoneNumber()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        var result = await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf());

        // Assert
        Assert.True(result.IsSuccess);
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.NotNull(customer);
        Assert.Null(customer.PhoneNumber);
    }

    [Fact]
    public async Task Given_InvalidPhoneNumber_When_Executing_Should_ReturnErrorAndNotPersistCustomer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        var result = await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), phoneNumber: "987654321");

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("PHONE_NUMBER_FORMAT_INVALID", notification.Code);
        Assert.Null(await _fixture.FindCustomerByEmailAsync(email));
    }

    [Fact]
    public async Task Given_NewCustomer_When_Executing_Should_PersistNormalizedCustomerWithSlug()
    {
        // Arrange
        var uniqueName = Guid.NewGuid().ToString("N");
        var email = CustomerTestData.NewEmail();
        var document = CustomerTestData.NewCpf();

        // Act
        var result = await ExecuteUseCaseAsync($"  João   {uniqueName} ", email.ToUpperInvariant(), document);

        // Assert
        Assert.True(result.IsSuccess);
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.NotNull(customer);
        Assert.Equal($"João {uniqueName}", customer.FullName);
        Assert.Equal(document, customer.Document);
        Assert.Equal($"joao-{uniqueName}", customer.Slug);
    }

    [Fact]
    public async Task Given_CorrelationId_When_Executing_Should_PersistItOnCustomer()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var email = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf(), correlationId);

        // Assert
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.NotNull(customer);
        Assert.Equal(correlationId, customer.CreatedCorrelationId);
    }

    [Fact]
    public async Task Given_Homonym_When_Executing_Should_PersistSlugWithRandomVersion()
    {
        // Arrange
        var fullName = CustomerTestData.NewFullName();
        var baseSlug = SlugValueObject.Build(fullName).Value;
        var email = CustomerTestData.NewEmail();
        await ExecuteUseCaseAsync(fullName, CustomerTestData.NewEmail(), CustomerTestData.NewCpf());

        // Act
        await ExecuteUseCaseAsync(fullName, email, CustomerTestData.NewCpf());

        // Assert
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.NotNull(customer);
        Assert.Matches($"^{baseSlug}-[a-z0-9]{{{SlugValueObject.RANDOM_VERSION_LENGTH}}}$", customer.Slug);
    }

    [Fact]
    public async Task Given_RegisteredEmail_When_Executing_Should_ReturnErrorAndKeepExistingCustomer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        var firstDocument = CustomerTestData.NewCpf();
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, firstDocument);

        // Act
        var result = await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf());

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CUSTOMER_ALREADY_EXISTS", notification.Code);
        var customer = await _fixture.FindCustomerByEmailAsync(email);
        Assert.Equal(firstDocument, customer?.Document);
    }

    [Fact]
    public async Task Given_RegisteredDocument_When_Executing_Should_ReturnErrorAndNotPersistNewCustomer()
    {
        // Arrange
        var document = CustomerTestData.NewCpf();
        var secondEmail = CustomerTestData.NewEmail();
        await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), CustomerTestData.NewEmail(), document);

        // Act
        var result = await ExecuteUseCaseAsync(CustomerTestData.NewFullName(), secondEmail, document);

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CUSTOMER_ALREADY_EXISTS", notification.Code);
        Assert.Null(await _fixture.FindCustomerByEmailAsync(secondEmail));
    }

    [Fact]
    public async Task Given_ConcurrentHomonyms_When_Executing_Should_PersistAllCustomersWithDistinctSlugs()
    {
        // Arrange
        const int CONCURRENT_CUSTOMERS = 5;
        var fullName = CustomerTestData.NewFullName();
        var emails = Enumerable.Range(0, CONCURRENT_CUSTOMERS).Select(_ => CustomerTestData.NewEmail()).ToArray();

        // Act
        var results = await Task.WhenAll(emails.Select(email => ExecuteUseCaseAsync(fullName, email, CustomerTestData.NewCpf())));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        var customers = await Task.WhenAll(emails.Select(_fixture.FindCustomerByEmailAsync));
        Assert.Equal(CONCURRENT_CUSTOMERS, customers.Select(customer => customer!.Slug).Distinct().Count());
    }

    [Fact]
    public async Task Given_ConcurrentRegistrationsWithSameEmail_When_Executing_Should_PersistOnlyOneCustomer()
    {
        // Arrange
        const int CONCURRENT_CUSTOMERS = 5;
        var email = CustomerTestData.NewEmail();

        // Act
        var results = await Task.WhenAll(Enumerable
            .Range(0, CONCURRENT_CUSTOMERS)
            .Select(_ => ExecuteUseCaseAsync(CustomerTestData.NewFullName(), email, CustomerTestData.NewCpf())));

        // Assert
        Assert.Single(results, result => result.IsSuccess);
        Assert.NotNull(await _fixture.FindCustomerByEmailAsync(email));
    }
}
