using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateProducer.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntegratedTests.UseCases;

[Collection(PostgreSqlCollection.NAME)]
public sealed class CreateProducerUseCaseTests
{
    private readonly PostgreSqlFixture _fixture;

    public CreateProducerUseCaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MethodResult> ExecuteUseCaseAsync(string name, string email, Guid? correlationId = null, string password = ProducerTestData.PASSWORD, CancellationToken cancellationToken = default)
    {
        using var scope = _fixture.Services.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<CreateProducerUseCaseInput>>();

        return await useCase.ExecuteAsync(
            input: CreateProducerUseCaseInput.Create(
                name: name,
                email: email,
                phone: ProducerTestData.PHONE,
                administratorName: "Maria da Silva",
                administratorPassword: password),
            auditableInfo: ProducerTestData.NewAuditableInfo(correlationId),
            cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_PersistPendingProducerAndPendingAdministrator()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();
        var name = ProducerTestData.NewName();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await ExecuteUseCaseAsync(name, email, correlationId);

        // Assert
        Assert.True(result.IsSuccess);
        var producer = await _fixture.FindProducerByEmailAsync(email);
        Assert.NotNull(producer);
        Assert.Equal("PENDING", producer.Status);
        Assert.Equal(ProducerTestData.E164_PHONE, producer.Phone);
        Assert.Equal(SlugValueObject.Build(name, SlugValueObject.SUBDOMAIN_MAX_LENGTH).Value, producer.Slug);
        Assert.Equal(correlationId, producer.CreatedCorrelationId);
        var administrator = await _fixture.FindProducerUserByEmailAsync(email);
        Assert.NotNull(administrator);
        Assert.Equal(producer.Id, administrator.ProducerId);
        Assert.Equal("ADMINISTRATOR", administrator.UserType);
        Assert.Equal("PENDING", administrator.Status);
        Assert.Equal(correlationId, administrator.CreatedCorrelationId);
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_RecordProducerRegisteredOnceInOutbox()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(ProducerTestData.NewName(), email);

        // Assert
        var producer = await _fixture.FindProducerByEmailAsync(email);
        var outboxEvent = Assert.Single(await _fixture.FindOutboxEventsByAggregateAsync(producer!.Id));
        Assert.Equal("ProducerRegistered", outboxEvent.EventType);
        using var payload = JsonDocument.Parse(outboxEvent.Payload);
        Assert.Equal($"producer:{producer.Id}", payload.RootElement.GetProperty("WalletIdempotencyKey").GetString());
    }

    [Fact]
    public async Task Given_ValidRegistration_When_Executing_Should_PersistOnlyHashOfAdministratorPassword()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(ProducerTestData.NewName(), email);

        // Assert
        var administrator = await _fixture.FindProducerUserByEmailAsync(email);
        var passwordHash = Rfc2898DeriveBytes.Pbkdf2(ProducerTestData.PASSWORD, administrator!.PasswordSalt, PasswordHashValueObject.ITERATIONS, HashAlgorithmName.SHA256, PasswordHashValueObject.LENGTH);
        Assert.Equal(HMACSHA256.HashData(Encoding.UTF8.GetBytes(CustomerTestData.PRIVATE_KEY), passwordHash), administrator.PasswordHash);
    }

    [Fact]
    public async Task Given_EmailAlreadyRegistered_When_Executing_Should_RejectAndPersistNothingNew()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();
        await ExecuteUseCaseAsync(ProducerTestData.NewName(), email);
        var otherName = ProducerTestData.NewName();

        // Act
        var result = await ExecuteUseCaseAsync(otherName, email);

        // Assert
        Assert.Equal("PRODUCER_EMAIL_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        Assert.NotEqual(otherName, (await _fixture.FindProducerByEmailAsync(email))!.Name);
    }

    [Fact]
    public async Task Given_SameNameTwice_When_Executing_Should_PersistDistinctSlugs()
    {
        // Arrange
        var name = ProducerTestData.NewName();
        var firstEmail = ProducerTestData.NewEmail();
        var secondEmail = ProducerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(name, firstEmail);
        await ExecuteUseCaseAsync(name, secondEmail);

        // Assert
        var first = await _fixture.FindProducerByEmailAsync(firstEmail);
        var second = await _fixture.FindProducerByEmailAsync(secondEmail);
        Assert.Matches(new Regex($"^{Regex.Escape(first!.Slug)}-[a-z0-9]{{5}}$"), second!.Slug);
    }

    [Fact]
    public async Task Given_ConcurrentHomonyms_When_Executing_Should_PersistAllProducersWithDistinctSlugs()
    {
        // Arrange
        const int CONCURRENT_PRODUCERS = 5;
        var name = ProducerTestData.NewName();
        var emails = Enumerable.Range(0, CONCURRENT_PRODUCERS).Select(_ => ProducerTestData.NewEmail()).ToArray();

        // Act
        var results = await Task.WhenAll(emails.Select(email => ExecuteUseCaseAsync(name, email)));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        var producers = await Task.WhenAll(emails.Select(_fixture.FindProducerByEmailAsync));
        Assert.Equal(CONCURRENT_PRODUCERS, producers.Select(producer => producer!.Slug).Distinct().Count());
    }

    [Fact]
    public async Task Given_ConcurrentRegistrationsWithSameEmail_When_Executing_Should_PersistOnlyOneProducerAndOneAdministrator()
    {
        // Arrange
        const int CONCURRENT_PRODUCERS = 5;
        var email = ProducerTestData.NewEmail();

        // Act
        var results = await Task.WhenAll(Enumerable
            .Range(0, CONCURRENT_PRODUCERS)
            .Select(_ => ExecuteUseCaseAsync(ProducerTestData.NewName(), email)));

        // Assert
        Assert.Single(results, result => result.IsSuccess);
        var producer = await _fixture.FindProducerByEmailAsync(email);
        Assert.Equal(producer!.Id, (await _fixture.FindProducerUserByEmailAsync(email))!.ProducerId);
        Assert.Single(await _fixture.FindOutboxEventsByAggregateAsync(producer.Id));
    }

    [Fact]
    public async Task Given_ReservedWordAsName_When_Executing_Should_PersistSlugWithSuffix()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync("Admin", email);

        // Assert
        Assert.Matches(new Regex("^admin-[a-z0-9]{5}$"), (await _fixture.FindProducerByEmailAsync(email))!.Slug);
    }

    [Fact]
    public async Task Given_WeakPassword_When_Executing_Should_PersistNothing()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();

        // Act
        var result = await ExecuteUseCaseAsync(ProducerTestData.NewName(), email, password: "senha");

        // Assert
        Assert.True(result.IsError);
        Assert.Null(await _fixture.FindProducerByEmailAsync(email));
        Assert.Null(await _fixture.FindProducerUserByEmailAsync(email));
    }

    [Fact]
    public async Task Given_CanceledRequest_When_Executing_Should_PersistNothing()
    {
        // Arrange
        var email = ProducerTestData.NewEmail();

        // Act
        var result = await ExecuteUseCaseAsync(ProducerTestData.NewName(), email, cancellationToken: new CancellationToken(canceled: true));

        // Assert
        Assert.Equal("CREATE_PRODUCER_CANCELED", Assert.Single(result.Notifications).Code);
        Assert.Null(await _fixture.FindProducerByEmailAsync(email));
    }
}
