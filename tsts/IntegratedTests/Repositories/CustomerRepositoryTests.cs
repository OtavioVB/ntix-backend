using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Enums;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Models;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace IntegratedTests.Repositories;

[Collection(PostgreSqlCollection.NAME)]
public sealed class CustomerRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public CustomerRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<T> InTransactionAsync<T>(Func<ICustomerRepository, Task<T>> action)
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        await unitOfWork.BeginTransactionAsync();

        var result = await action(repository);

        await unitOfWork.CommitAsync();

        return result;
    }

    private Task<CustomerCreationResult> CreateAsync(CustomerModel customer)
        => InTransactionAsync(repository => repository.CreateAsync(customer));

    [Fact]
    public async Task Given_NewCustomer_When_Creating_Should_PersistCustomer()
    {
        // Arrange
        var customer = CustomerTestData.NewModel(phoneNumber: "11987654321");

        // Act
        var result = await CreateAsync(customer);

        // Assert
        Assert.Equal(CustomerCreationResult.Created, result);
        var persisted = await _fixture.FindCustomerByEmailAsync(customer.Email);
        Assert.NotNull(persisted);
        Assert.Equal(customer.Id, persisted.Id);
        Assert.Equal(customer.FullName, persisted.FullName);
        Assert.Equal(customer.Document, persisted.Document);
        Assert.Equal(customer.Slug, persisted.Slug);
        Assert.Equal(customer.PasswordHash, persisted.PasswordHash);
        Assert.Equal(customer.PasswordSalt, persisted.PasswordSalt);
        Assert.Equal(customer.PhoneNumber, persisted.PhoneNumber);
        Assert.Equal(customer.CreatedCorrelationId, persisted.CreatedCorrelationId);
    }

    [Fact]
    public async Task Given_CustomerWithoutPhoneNumber_When_Creating_Should_PersistNull()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();

        // Act
        await CreateAsync(customer);

        // Assert
        var persisted = await _fixture.FindCustomerByEmailAsync(customer.Email);
        Assert.NotNull(persisted);
        Assert.Null(persisted.PhoneNumber);
    }

    [Fact]
    public async Task Given_RegisteredEmail_When_CheckingExistence_Should_ReturnTrue()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();
        await CreateAsync(customer);

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsByEmailOrDocumentAsync(customer.Email, CustomerTestData.NewCpf()));

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task Given_RegisteredDocument_When_CheckingExistence_Should_ReturnTrue()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();
        await CreateAsync(customer);

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsByEmailOrDocumentAsync(CustomerTestData.NewEmail(), customer.Document));

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task Given_UnknownEmailAndDocument_When_CheckingExistence_Should_ReturnFalse()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        var document = CustomerTestData.NewCpf();

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsByEmailOrDocumentAsync(email, document));

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task Given_RegisteredSlug_When_CheckingExistence_Should_ReturnTrue()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();
        await CreateAsync(customer);

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsBySlugAsync(customer.Slug));

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task Given_SlugThatOnlySharesPrefix_When_CheckingExistence_Should_ReturnFalse()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();
        await CreateAsync(customer);

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsBySlugAsync(customer.Slug[..^1]));

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task Given_CustomerWithSameEmail_When_Creating_Should_ReturnEmailOrDocumentAlreadyExists()
    {
        // Arrange
        var existing = CustomerTestData.NewModel();
        await CreateAsync(existing);

        // Act
        var result = await CreateAsync(CustomerTestData.NewModel(email: existing.Email));

        // Assert
        Assert.Equal(CustomerCreationResult.EmailOrDocumentAlreadyExists, result);
    }

    [Fact]
    public async Task Given_CustomerWithSameDocument_When_Creating_Should_ReturnEmailOrDocumentAlreadyExists()
    {
        // Arrange
        var existing = CustomerTestData.NewModel();
        await CreateAsync(existing);

        // Act
        var result = await CreateAsync(CustomerTestData.NewModel(document: existing.Document));

        // Assert
        Assert.Equal(CustomerCreationResult.EmailOrDocumentAlreadyExists, result);
    }

    [Fact]
    public async Task Given_CustomerWithSameSlug_When_Creating_Should_ReturnSlugAlreadyExists()
    {
        // Arrange
        var existing = CustomerTestData.NewModel();
        await CreateAsync(existing);

        // Act
        var result = await CreateAsync(CustomerTestData.NewModel(slug: existing.Slug));

        // Assert
        Assert.Equal(CustomerCreationResult.SlugAlreadyExists, result);
    }

    [Fact]
    public async Task Given_SlugConflictInTransaction_When_CreatingWithOtherSlugInSameTransaction_Should_PersistCustomer()
    {
        // Arrange
        var existing = CustomerTestData.NewModel();
        await CreateAsync(existing);
        var customer = CustomerTestData.NewModel(slug: existing.Slug);

        // Act
        var result = await InTransactionAsync(async repository =>
        {
            await repository.CreateAsync(customer);

            return await repository.CreateAsync(customer with { Slug = $"cliente-{Guid.NewGuid():N}" });
        });

        // Assert
        Assert.Equal(CustomerCreationResult.Created, result);
        Assert.NotNull(await _fixture.FindCustomerByEmailAsync(customer.Email));
    }
}
