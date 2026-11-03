using Infra.PostgreSql.Base.UnitOfWork.Interfaces;
using Infra.PostgreSql.Repositories.Customers.Interfaces;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace IntegratedTests.UnitOfWork;

[Collection(PostgreSqlCollection.NAME)]
public sealed class PostgreSqlUnitOfWorkTests
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlUnitOfWorkTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Given_ActiveTransaction_When_Committing_Should_PersistChanges()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var customer = CustomerTestData.NewModel();
        await unitOfWork.BeginTransactionAsync();
        await repository.CreateAsync(customer);

        // Act
        await unitOfWork.CommitAsync();

        // Assert
        Assert.NotNull(await _fixture.FindCustomerByEmailAsync(customer.Email));
    }

    [Fact]
    public async Task Given_ActiveTransaction_When_RollingBack_Should_DiscardChanges()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var customer = CustomerTestData.NewModel();
        await unitOfWork.BeginTransactionAsync();
        await repository.CreateAsync(customer);

        // Act
        await unitOfWork.RollbackAsync();

        // Assert
        Assert.Null(await _fixture.FindCustomerByEmailAsync(customer.Email));
    }

    [Fact]
    public async Task Given_TransactionWithoutCommit_When_DisposingScope_Should_DiscardChanges()
    {
        // Arrange
        var customer = CustomerTestData.NewModel();

        // Act
        using (var scope = _fixture.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().CreateAsync(customer);
        }

        // Assert
        Assert.Null(await _fixture.FindCustomerByEmailAsync(customer.Email));
    }

    [Fact]
    public async Task Given_NoActiveTransaction_When_RollingBack_Should_DoNothing()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Act
        var exception = await Record.ExceptionAsync(() => unitOfWork.RollbackAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Given_NoActiveTransaction_When_Committing_Should_ThrowInvalidOperationException()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Act
        var commit = () => unitOfWork.CommitAsync();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(commit);
    }

    [Fact]
    public async Task Given_NoActiveTransaction_When_ExecutingRepository_Should_ThrowInvalidOperationException()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();

        // Act
        var create = () => repository.CreateAsync(CustomerTestData.NewModel());

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(create);
    }

    [Fact]
    public async Task Given_ActiveTransaction_When_BeginningAnother_Should_ThrowInvalidOperationException()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync();

        // Act
        var begin = () => unitOfWork.BeginTransactionAsync();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(begin);
    }
}
