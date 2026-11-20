using Application.UseCases.Base.Customers.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer.Inputs;
using Application.UseCases.CreateCustomerSession.Inputs;
using Application.UseCases.GetCurrentCustomer.Inputs;
using Application.UseCases.RefreshCustomerSession.Inputs;
using Application.UseCases.RevokeCustomerSession.Inputs;
using Domain.CrossCutting.MethodResultContext;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

/// <summary>Sessão do cliente (REQ12) contra o PostgreSQL real: login por e-mail e CPF, rotação, reuso e logout.</summary>
[Collection(PostgreSqlCollection.NAME)]
public sealed class CustomerSessionUseCasesTests
{
    private const string COUNT_ACTIVE_SESSIONS_SQL = "SELECT COUNT(*)::int FROM customers.customer_session WHERE id_customer = @id AND revoked_at IS NULL;";
    private const string SELECT_HASHES_SQL = "SELECT string_agg(encode(refresh_token_hash, 'base64'), '|') FROM customers.customer_session WHERE id_customer = @id;";

    private readonly PostgreSqlFixture _fixture;

    public CustomerSessionUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MethodResult<TOutput>> ExecuteAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<TInput, TOutput>>().ExecuteAsync(input, CustomerTestData.NewAuditableInfo());
    }

    private async Task<MethodResult> ExecuteAsync<TInput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUseCase<TInput>>().ExecuteAsync(input, CustomerTestData.NewAuditableInfo());
    }

    private async Task<(string Email, string Cpf)> GivenCustomerAsync()
    {
        var email = CustomerTestData.NewEmail();
        var cpf = CustomerTestData.NewCpf();

        var created = await ExecuteAsync(CreateCustomerUseCaseInput.Create(CustomerTestData.NewFullName(), email, cpf, CustomerTestData.PASSWORD, null));

        Assert.True(created.IsSuccess);

        return (email, cpf);
    }

    private Task<MethodResult<CustomerSessionOutput>> LoginAsync(string login, string password = CustomerTestData.PASSWORD)
        => ExecuteAsync<CreateCustomerSessionUseCaseInput, CustomerSessionOutput>(CreateCustomerSessionUseCaseInput.Create(login, password));

    private Task<MethodResult<CustomerSessionOutput>> RefreshAsync(string refreshToken)
        => ExecuteAsync<RefreshCustomerSessionUseCaseInput, CustomerSessionOutput>(RefreshCustomerSessionUseCaseInput.Create(refreshToken));

    private Task<int> CountActiveSessionsAsync(Guid customerId)
        => _fixture.ScalarAsync<int>(COUNT_ACTIVE_SESSIONS_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = customerId });

    [Fact]
    public async Task Given_CustomerWithMaskedCpf_When_LoggingIn_Should_PersistOnlyTheTokenHash()
    {
        // Arrange
        var (_, cpf) = await GivenCustomerAsync();
        var masked = $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

        // Act
        var session = await LoginAsync(masked);

        // Assert
        Assert.True(session.IsSuccess);
        var stored = await _fixture.ScalarAsync<string>(SELECT_HASHES_SQL, new NpgsqlParameter("@id", NpgsqlDbType.Uuid) { Value = session.Output!.Customer.Id });
        Assert.DoesNotContain(session.Output.RefreshToken, stored);
        Assert.Equal(1, await CountActiveSessionsAsync(session.Output.Customer.Id));

        var me = await ExecuteAsync<GetCurrentCustomerUseCaseInput, CurrentCustomerOutput>(GetCurrentCustomerUseCaseInput.Create(session.Output.Customer.Id.ToString()));
        Assert.Equal($"***.{cpf[3..6]}.{cpf[6..9]}-**", me.Output!.Document);
    }

    [Fact]
    public async Task Given_WrongPassword_When_LoggingInByEmail_Should_Refuse()
    {
        // Arrange
        var (email, _) = await GivenCustomerAsync();

        // Act
        var session = await LoginAsync(email.ToUpperInvariant(), "Senha@errada1");

        // Assert
        Assert.Equal("INVALID_CREDENTIALS", Assert.Single(session.Notifications).Code);
    }

    [Fact]
    public async Task Given_Session_When_RefreshingAndReusingTheOldToken_Should_RotateThenRevokeTheFamily()
    {
        // Arrange
        var (email, _) = await GivenCustomerAsync();
        var first = (await LoginAsync(email)).Output!;

        // Act
        var second = await RefreshAsync(first.RefreshToken);
        var reused = await RefreshAsync(first.RefreshToken);
        var afterReuse = await RefreshAsync(second.Output!.RefreshToken);

        // Assert
        Assert.True(second.IsSuccess);
        Assert.Equal("INVALID_REFRESH_TOKEN", Assert.Single(reused.Notifications).Code);
        Assert.Equal("INVALID_REFRESH_TOKEN", Assert.Single(afterReuse.Notifications).Code);
        Assert.Equal(0, await CountActiveSessionsAsync(first.Customer.Id));
    }

    [Fact]
    public async Task Given_Session_When_LoggingOut_Should_RevokeItAndKeepOtherLoginsActive()
    {
        // Arrange
        var (email, cpf) = await GivenCustomerAsync();
        var onPhone = (await LoginAsync(email)).Output!;
        await LoginAsync(cpf);

        // Act
        var revoked = await ExecuteAsync(RevokeCustomerSessionUseCaseInput.Create(onPhone.RefreshToken));

        // Assert
        Assert.True(revoked.IsSuccess);
        Assert.Equal("INVALID_REFRESH_TOKEN", Assert.Single((await RefreshAsync(onPhone.RefreshToken)).Notifications).Code);
        Assert.Equal(1, await CountActiveSessionsAsync(onPhone.Customer.Id));
    }
}
