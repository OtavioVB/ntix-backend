using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateProducer.Inputs;
using Domain.CrossCutting.MethodResultContext;
using IntegratedTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IntegratedTests.UseCases;

[Collection(SqlServerCollection.NAME)]
public sealed class CreateProducerUseCaseTests
{
    private const string PHONE = "+5511999999999";
    private const string USER_NAME = "Maria da Silva";

    private const string COUNT_PRODUCERS_SQL = "SELECT COUNT(*) FROM producers.Producer WHERE email = @email;";
    private const string COUNT_USERS_SQL = "SELECT COUNT(*) FROM producers.ProducerUser WHERE email = @email;";
    private const string SELECT_SLUG_SQL = "SELECT slug FROM producers.Producer WHERE email = @email;";

    private const string COUNT_PENDING_PRODUCERS_SQL = """
        SELECT COUNT(*) FROM producers.Producer
        WHERE email = @email AND status = 'PENDING' AND phone = @phone AND producer_type IS NULL AND created_correlation_id = @correlation_id;
        """;

    private const string COUNT_PENDING_ADMINISTRATORS_SQL = """
        SELECT COUNT(*)
        FROM producers.ProducerUser AS producer_user
        INNER JOIN producers.Producer AS producer ON producer.id_producer = producer_user.producer_id
        WHERE producer_user.email = @email AND producer.email = @email
          AND producer_user.user_type = 'ADMINISTRATOR' AND producer_user.status = 'PENDING'
          AND producer_user.created_correlation_id = @correlation_id;
        """;

    private const string COUNT_VERIFICATIONS_BY_HASH_SQL = """
        SELECT COUNT(*)
        FROM producers.EmailVerification AS verification
        INNER JOIN producers.ProducerUser AS producer_user ON producer_user.id_producer_user = verification.producer_user_id
        WHERE producer_user.email = @email AND verification.token_hash = @token_hash
          AND DATEDIFF(HOUR, verification.created_at, verification.expires_at) = 24;
        """;

    private const string INSERT_USER_OF_OTHER_PRODUCER_SQL = """
        INSERT INTO producers.ProducerUser (id_producer_user, producer_id, name, email, password_hash, password_salt, user_type, status, created_at, created_correlation_id)
        VALUES (NEWID(), @producer_id, 'Outro usuário', @email, 0x00, 0x00, 'ADMINISTRATOR', 'ACTIVE', SYSUTCDATETIME(), NEWID());
        """;

    private readonly SqlServerFixture _fixture;

    public CreateProducerUseCaseTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string NewName()
        => $"Produtora {Guid.NewGuid():N}";

    private static string NewCnpj()
    {
        var digits = string.Concat(Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)));

        digits += CalculateCnpjCheckDigit(digits);
        digits += CalculateCnpjCheckDigit(digits);

        return digits;
    }

    private static int CalculateCnpjCheckDigit(string digits)
    {
        var sum = digits.Reverse().Select((digit, index) => (digit - '0') * (index % 8 + 2)).Sum();
        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }

    private async Task<MethodResult> ExecuteUseCaseAsync(string name, string email, Guid? correlationId = null, string? document = null)
    {
        using var scope = _fixture.Services.CreateScope();

        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<CreateProducerUseCaseInput>>();

        return await useCase.ExecuteAsync(
            input: CreateProducerUseCaseInput.Create(
                name: name,
                email: email,
                phone: PHONE,
                document: document,
                userName: USER_NAME,
                password: CustomerTestData.PASSWORD),
            auditableInfo: CustomerTestData.NewAuditableInfo(correlationId));
    }

    private Task<int> CountAsync(string sql, string email)
        => _fixture.Database.ScalarAsync<int>(sql, new SqlParameter("@email", email));

    private Task<string> GetSlugAsync(string email)
        => _fixture.Database.ScalarAsync<string>(SELECT_SLUG_SQL, new SqlParameter("@email", email));

    [Fact]
    public async Task Given_NewProducer_When_Executing_Should_PersistPendingProducerAndAdministrator()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        var correlationId = Guid.NewGuid();

        // Act
        var result = await ExecuteUseCaseAsync(NewName(), email, correlationId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, await _fixture.Database.ScalarAsync<int>(
            COUNT_PENDING_PRODUCERS_SQL,
            new SqlParameter("@email", email),
            new SqlParameter("@phone", PHONE),
            new SqlParameter("@correlation_id", correlationId)));
        Assert.Equal(1, await _fixture.Database.ScalarAsync<int>(
            COUNT_PENDING_ADMINISTRATORS_SQL,
            new SqlParameter("@email", email),
            new SqlParameter("@correlation_id", correlationId)));
    }

    [Fact]
    public async Task Given_NewProducer_When_Executing_Should_SendTokenAndPersistOnlyItsHash()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync(NewName(), email);

        // Assert
        var sentEmail = Assert.Single(_fixture.EmailSender.Sent, sent => sent.To == email);
        var token = Regex.Match(sentEmail.Html, "token=(?<token>[a-f0-9]{64})").Groups["token"].Value;
        Assert.Equal(1, await _fixture.Database.ScalarAsync<int>(
            COUNT_VERIFICATIONS_BY_HASH_SQL,
            new SqlParameter("@email", email),
            new SqlParameter("@token_hash", SHA256.HashData(Encoding.UTF8.GetBytes(token)))));
    }

    [Fact]
    public async Task Given_EmailSendingFailure_When_Executing_Should_KeepProducer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        _fixture.EmailSender.FailFor(email);

        // Act
        var result = await ExecuteUseCaseAsync(NewName(), email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, await CountAsync(COUNT_PRODUCERS_SQL, email));
    }

    [Fact]
    public async Task Given_RegisteredProducerEmail_When_Executing_Should_ReturnErrorAndNotPersist()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        await ExecuteUseCaseAsync(NewName(), email);

        // Act
        var result = await ExecuteUseCaseAsync(NewName(), email);

        // Assert
        Assert.Equal("PRODUCER_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        Assert.Equal(1, await CountAsync(COUNT_PRODUCERS_SQL, email));
        Assert.Equal(1, await CountAsync(COUNT_USERS_SQL, email));
    }

    [Fact]
    public async Task Given_EmailUsedOnlyByUser_When_Executing_Should_ReturnErrorAndNotPersistProducer()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();
        await _fixture.Database.ExecuteAsync(
            INSERT_USER_OF_OTHER_PRODUCER_SQL,
            new SqlParameter("@producer_id", await _fixture.CreateProducerAsync()),
            new SqlParameter("@email", email));

        // Act
        var result = await ExecuteUseCaseAsync(NewName(), email);

        // Assert
        Assert.Equal("PRODUCER_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        Assert.Equal(0, await CountAsync(COUNT_PRODUCERS_SQL, email));
    }

    [Fact]
    public async Task Given_ProducersWithoutDocument_When_Executing_Should_PersistAll()
    {
        // Act
        var first = await ExecuteUseCaseAsync(NewName(), CustomerTestData.NewEmail());
        var second = await ExecuteUseCaseAsync(NewName(), CustomerTestData.NewEmail());

        // Assert
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task Given_RegisteredDocument_When_Executing_Should_ReturnErrorAndNotPersist()
    {
        // Arrange
        var document = NewCnpj();
        var email = CustomerTestData.NewEmail();
        await ExecuteUseCaseAsync(NewName(), CustomerTestData.NewEmail(), document: document);

        // Act
        var result = await ExecuteUseCaseAsync(NewName(), email, document: document);

        // Assert
        Assert.Equal("PRODUCER_DOCUMENT_ALREADY_EXISTS", Assert.Single(result.Notifications).Code);
        Assert.Equal(0, await CountAsync(COUNT_PRODUCERS_SQL, email));
    }

    [Fact]
    public async Task Given_ConcurrentSameDocument_When_Executing_Should_PersistOnlyOneProducer()
    {
        // Arrange
        const int CONCURRENT_PRODUCERS = 5;
        var document = NewCnpj();

        // Act
        var results = await Task.WhenAll(Enumerable
            .Range(0, CONCURRENT_PRODUCERS)
            .Select(_ => ExecuteUseCaseAsync(NewName(), CustomerTestData.NewEmail(), document: document)));

        // Assert
        Assert.Single(results, result => result.IsSuccess);
    }

    [Fact]
    public async Task Given_SameName_When_Executing_Should_PersistRandomVersionOfSlug()
    {
        // Arrange
        var name = NewName();
        var firstEmail = CustomerTestData.NewEmail();
        var secondEmail = CustomerTestData.NewEmail();
        await ExecuteUseCaseAsync(name, firstEmail);

        // Act
        await ExecuteUseCaseAsync(name, secondEmail);

        // Assert
        Assert.Matches($"^{await GetSlugAsync(firstEmail)}-[a-z0-9]{{5}}$", await GetSlugAsync(secondEmail));
    }

    [Fact]
    public async Task Given_ReservedName_When_Executing_Should_PersistSlugWithVersion()
    {
        // Arrange
        var email = CustomerTestData.NewEmail();

        // Act
        await ExecuteUseCaseAsync("Admin", email);

        // Assert
        Assert.Matches("^admin-[a-z0-9]{5}$", await GetSlugAsync(email));
    }

    [Fact]
    public async Task Given_ConcurrentSameName_When_Executing_Should_PersistAllWithDistinctSlugs()
    {
        // Arrange
        const int CONCURRENT_PRODUCERS = 5;
        var name = NewName();
        var emails = Enumerable.Range(0, CONCURRENT_PRODUCERS).Select(_ => CustomerTestData.NewEmail()).ToArray();

        // Act
        var results = await Task.WhenAll(emails.Select(email => ExecuteUseCaseAsync(name, email)));

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        var slugs = await Task.WhenAll(emails.Select(GetSlugAsync));
        Assert.Equal(CONCURRENT_PRODUCERS, slugs.Distinct().Count());
    }

    [Fact]
    public async Task Given_ConcurrentSameEmail_When_Executing_Should_PersistOnlyOneProducerAndUser()
    {
        // Arrange
        const int CONCURRENT_PRODUCERS = 5;
        var email = CustomerTestData.NewEmail();

        // Act
        var results = await Task.WhenAll(Enumerable
            .Range(0, CONCURRENT_PRODUCERS)
            .Select(_ => ExecuteUseCaseAsync(NewName(), email)));

        // Assert
        Assert.Single(results, result => result.IsSuccess);
        Assert.Equal(1, await CountAsync(COUNT_PRODUCERS_SQL, email));
        Assert.Equal(1, await CountAsync(COUNT_USERS_SQL, email));
    }
}
