using Application.UseCases.AuthenticateCustomer.Inputs;
using Application.UseCases.AuthenticateCustomer.Outputs;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.CreateCustomer.Inputs;
using Domain.CrossCutting.MethodResultContext;
using Infra.Jwt.Base.TokenGenerator;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace IntegratedTests.UseCases;

[Collection(SqlServerCollection.NAME)]
public sealed class AuthenticateCustomerUseCaseTests
{
    private const string AUTHENTICATION_FAILED = "AUTHENTICATION_FAILED";

    private readonly SqlServerFixture _fixture;

    public AuthenticateCustomerUseCaseTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record RegisteredCustomer(Guid Id, string Email, string Document);

    private async Task<RegisteredCustomer> GivenRegisteredCustomerAsync()
    {
        var email = CustomerTestData.NewEmail();
        var document = CustomerTestData.NewCpf();

        using var scope = _fixture.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IUseCase<CreateCustomerUseCaseInput>>()
            .ExecuteAsync(
                input: CreateCustomerUseCaseInput.Create(
                    fullName: CustomerTestData.NewFullName(),
                    email: email,
                    document: document,
                    password: CustomerTestData.PASSWORD,
                    phoneNumber: null as string),
                auditableInfo: CustomerTestData.NewAuditableInfo());

        Assert.True(result.IsSuccess);

        var customer = await _fixture.FindCustomerByEmailAsync(email);

        return new RegisteredCustomer(customer!.Id, email, document);
    }

    private async Task<(RegisteredCustomer Customer, Guid ProducerId)> GivenLinkedCustomerAsync()
    {
        var customer = await GivenRegisteredCustomerAsync();
        var producerId = await _fixture.CreateProducerAsync();

        await _fixture.LinkCustomerToProducerAsync(customer.Id, producerId);

        return (customer, producerId);
    }

    private async Task<MethodResult<AuthenticateCustomerUseCaseOutput>> AuthenticateAsync(string? email, string? document, Guid producerId, string password = CustomerTestData.PASSWORD)
    {
        using var scope = _fixture.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IUseCase<AuthenticateCustomerUseCaseInput, AuthenticateCustomerUseCaseOutput>>()
            .ExecuteAsync(
                input: AuthenticateCustomerUseCaseInput.Create(email, document, password, producerId.ToString()),
                auditableInfo: CustomerTestData.NewAuditableInfo());
    }

    [Fact]
    public async Task Given_LinkedCustomerWithEmail_When_Executing_Should_ReturnSignedTokenWithProducerValidForFourHours()
    {
        // Arrange
        var (customer, producerId) = await GivenLinkedCustomerAsync();

        // Act
        var result = await AuthenticateAsync(customer.Email, null, producerId);

        // Assert
        Assert.True(result.IsSuccess);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.Output!.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = JwtTokenGenerator.ISSUER,
            ValidAudience = JwtTokenGenerator.AUDIENCE,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomerTestData.JWT_SECRET_KEY)),
        });
        Assert.True(validation.IsValid);
        var token = new JsonWebToken(result.Output.AccessToken);
        Assert.Equal(customer.Id.ToString(), token.Subject);
        Assert.Equal(producerId.ToString(), token.GetClaim(JwtTokenGenerator.PRODUCER_ID_CLAIM).Value);
        Assert.Equal(TimeSpan.FromHours(4), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task Given_LinkedCustomerWithDocument_When_Executing_Should_Authenticate()
    {
        // Arrange
        var (customer, producerId) = await GivenLinkedCustomerAsync();

        // Act
        var result = await AuthenticateAsync(null, customer.Document, producerId);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Given_WrongPassword_When_Executing_Should_ReturnFailure()
    {
        // Arrange
        var (customer, producerId) = await GivenLinkedCustomerAsync();

        // Act
        var result = await AuthenticateAsync(customer.Email, null, producerId, password: "Errada@123");

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
    }

    [Fact]
    public async Task Given_UnknownEmail_When_Executing_Should_ReturnFailure()
    {
        // Arrange
        var producerId = await _fixture.CreateProducerAsync();

        // Act
        var result = await AuthenticateAsync(CustomerTestData.NewEmail(), null, producerId);

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
    }

    [Fact]
    public async Task Given_CustomerLinkedToOtherProducer_When_Executing_Should_ReturnFailure()
    {
        // Arrange
        var (customer, _) = await GivenLinkedCustomerAsync();
        var otherProducerId = await _fixture.CreateProducerAsync();

        // Act
        var result = await AuthenticateAsync(customer.Email, null, otherProducerId);

        // Assert
        var notification = Assert.Single(result.Notifications);
        Assert.Equal(AUTHENTICATION_FAILED, notification.Code);
    }

    [Fact]
    public async Task Given_CustomerLinkedToTwoProducers_When_Executing_Should_ReturnTokenOfRequestedProducer()
    {
        // Arrange
        var (customer, _) = await GivenLinkedCustomerAsync();
        var secondProducerId = await _fixture.CreateProducerAsync();
        await _fixture.LinkCustomerToProducerAsync(customer.Id, secondProducerId);

        // Act
        var result = await AuthenticateAsync(customer.Email, null, secondProducerId);

        // Assert
        var token = new JsonWebToken(result.Output!.AccessToken);
        Assert.Equal(secondProducerId.ToString(), token.GetClaim(JwtTokenGenerator.PRODUCER_ID_CLAIM).Value);
    }
}
