using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Producers.Enums;
using Infra.PostgreSql.Repositories.Producers.Interfaces;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace IntegratedTests.Repositories;

[Collection(PostgreSqlCollection.NAME)]
public sealed class ProducerRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public ProducerRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<T> InTransactionAsync<T>(Func<IProducerRepository, Task<T>> action)
    {
        using var scope = _fixture.Services.CreateScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IProducerRepository>();

        await unitOfWork.BeginTransactionAsync();

        var result = await action(repository);

        await unitOfWork.CommitAsync();

        return result;
    }

    [Fact]
    public async Task Given_NewProducerAndAdministrator_When_Creating_Should_PersistBoth()
    {
        // Arrange
        var producer = ProducerTestData.NewModel();
        var administrator = ProducerTestData.NewUserModel(producer.Id, producer.Email);

        // Act
        var results = await InTransactionAsync(async repository =>
            (await repository.CreateAsync(producer), await repository.CreateUserAsync(administrator)));

        // Assert
        Assert.Equal(ProducerCreationResult.Created, results.Item1);
        Assert.Equal(ProducerUserCreationResult.Created, results.Item2);
        var persistedProducer = await _fixture.FindProducerByEmailAsync(producer.Email);
        Assert.Equal(producer with { CreatedAt = persistedProducer!.CreatedAt }, persistedProducer);
        var persistedUser = await _fixture.FindProducerUserByEmailAsync(producer.Email);
        Assert.Equal(producer.Id, persistedUser!.ProducerId);
        Assert.Equal("ADMINISTRATOR", persistedUser.UserType);
        Assert.Equal("PENDING", persistedUser.Status);
    }

    [Fact]
    public async Task Given_EmailOfAProducer_When_CheckingExistence_Should_ReturnTrue()
    {
        // Arrange
        var producer = ProducerTestData.NewModel();
        await InTransactionAsync(repository => repository.CreateAsync(producer));

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsByEmailAsync(producer.Email));

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task Given_EmailOfAProducerUserOnly_When_CheckingExistence_Should_ReturnTrue()
    {
        // Arrange
        var producer = ProducerTestData.NewModel();
        var userEmail = ProducerTestData.NewEmail();
        await InTransactionAsync(async repository =>
        {
            await repository.CreateAsync(producer);
            return await repository.CreateUserAsync(ProducerTestData.NewUserModel(producer.Id, userEmail));
        });

        // Act
        var exists = await InTransactionAsync(repository => repository.ExistsByEmailAsync(userEmail));

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task Given_UnknownEmailAndSlug_When_CheckingExistence_Should_ReturnFalse()
    {
        // Arrange & Act
        var emailExists = await InTransactionAsync(repository => repository.ExistsByEmailAsync(ProducerTestData.NewEmail()));
        var slugExists = await InTransactionAsync(repository => repository.ExistsBySlugAsync($"p-{Guid.NewGuid():N}"));

        // Assert
        Assert.False(emailExists);
        Assert.False(slugExists);
    }

    [Fact]
    public async Task Given_DuplicatedSlug_When_Creating_Should_ReturnSlugAlreadyExistsAndKeepTransactionUsable()
    {
        // Arrange
        var existing = ProducerTestData.NewModel();
        await InTransactionAsync(repository => repository.CreateAsync(existing));
        var duplicated = ProducerTestData.NewModel(slug: existing.Slug);
        var retry = ProducerTestData.NewModel(email: duplicated.Email);

        // Act
        var results = await InTransactionAsync(async repository =>
            (await repository.CreateAsync(duplicated), await repository.CreateAsync(retry)));

        // Assert
        Assert.Equal(ProducerCreationResult.SlugAlreadyExists, results.Item1);
        Assert.Equal(ProducerCreationResult.Created, results.Item2);
        Assert.Equal(retry.Slug, (await _fixture.FindProducerByEmailAsync(retry.Email))!.Slug);
    }

    [Fact]
    public async Task Given_DuplicatedProducerEmail_When_Creating_Should_ReturnEmailAlreadyExists()
    {
        // Arrange
        var existing = ProducerTestData.NewModel();
        await InTransactionAsync(repository => repository.CreateAsync(existing));

        // Act
        var result = await InTransactionAsync(repository => repository.CreateAsync(ProducerTestData.NewModel(email: existing.Email)));

        // Assert
        Assert.Equal(ProducerCreationResult.EmailAlreadyExists, result);
    }

    [Fact]
    public async Task Given_DuplicatedUserEmail_When_CreatingUser_Should_ReturnEmailAlreadyExists()
    {
        // Arrange
        var producer = ProducerTestData.NewModel();
        await InTransactionAsync(async repository =>
        {
            await repository.CreateAsync(producer);
            return await repository.CreateUserAsync(ProducerTestData.NewUserModel(producer.Id, producer.Email));
        });

        // Act
        var result = await InTransactionAsync(repository => repository.CreateUserAsync(ProducerTestData.NewUserModel(producer.Id, producer.Email)));

        // Assert
        Assert.Equal(ProducerUserCreationResult.EmailAlreadyExists, result);
    }
}
