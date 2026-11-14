using Domain.BoundedContexts.PaymentContext.Entities;
using Domain.BoundedContexts.PaymentContext.Enums;
using Domain.ValueObjects;
using UnitTests.Support;

namespace UnitTests.Payments;

public sealed class ProducerIntegrationTests
{
    private static Dictionary<string, CredentialValueObject?> Credentials(params (string Key, string? Value)[] credentials)
        => credentials.ToDictionary(credential => credential.Key, credential => (CredentialValueObject?)CredentialValueObject.Build(credential.Value));

    private static Domain.CrossCutting.MethodResultContext.MethodResult<ProducerIntegration> Create(IReadOnlyDictionary<string, CredentialValueObject?> credentials, Provider? provider = null)
        => ProducerIntegration.Create(
            producerId: PaymentTestData.PRODUCER_ID,
            provider: provider ?? PaymentTestData.AbacatePay(),
            name: "AbacatePay - conta principal",
            credentials: credentials,
            now: PaymentTestData.NOW,
            correlationId: PaymentTestData.CORRELATION_ID);

    [Fact]
    public void Given_AllRequiredCredentials_When_Creating_Should_CreateActiveIntegration()
    {
        // Arrange
        var credentials = Credentials((PaymentTestData.API_KEY, PaymentTestData.API_KEY_VALUE));

        // Act
        var result = Create(credentials);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationStatus.ACTIVE, result.Output!.Status);
        Assert.Equal([PaymentTestData.API_KEY], result.Output.CredentialKeys);
        Assert.Equal(PaymentTestData.PRODUCER_ID.Value, result.Output.ProducerId.Value);
        Assert.Equal(PaymentTestData.CORRELATION_ID.Value, result.Output.UpdatedCorrelationId.Value);
    }

    [Fact]
    public void Given_MissingRequiredCredential_When_Creating_Should_ReturnCredentialKeyRequired()
    {
        // Arrange
        var credentials = Credentials((PaymentTestData.WEBHOOK_SECRET, "segredo-do-webhook-com-32-caracteres"));

        // Act
        var result = Create(credentials);

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CREDENTIAL_KEY_REQUIRED", notification.Code);
        Assert.Contains(PaymentTestData.API_KEY, notification.Message);
    }

    [Fact]
    public void Given_UnknownCredentialKey_When_Creating_Should_ReturnCredentialKeyUnknownWithoutEchoingTheKey()
    {
        // Arrange
        var credentials = Credentials((PaymentTestData.API_KEY, PaymentTestData.API_KEY_VALUE), ("SENHA", "123456"));

        // Act
        var result = Create(credentials);

        // Assert
        Assert.True(result.IsError);
        var notification = Assert.Single(result.Notifications);
        Assert.Equal("CREDENTIAL_KEY_UNKNOWN", notification.Code);
        Assert.DoesNotContain("SENHA", notification.Message);
    }

    [Fact]
    public void Given_KeyWithDifferentCase_When_Creating_Should_ReturnCredentialKeyUnknown()
    {
        // Arrange
        var credentials = Credentials(("api_key", PaymentTestData.API_KEY_VALUE));

        // Act
        var result = Create(credentials);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_UNKNOWN");
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" abc_dev_123")]
    public void Given_InvalidCredentialValue_When_Creating_Should_ReturnCredentialValueInvalid(string? value)
    {
        // Arrange
        var credentials = Credentials((PaymentTestData.API_KEY, value));

        // Act
        var result = Create(credentials);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_VALUE_INVALID");
    }

    [Fact]
    public void Given_NoCredentials_When_Creating_Should_ReturnEveryRequiredKey()
    {
        // Arrange & Act
        var result = Create(Credentials(), PaymentTestData.PixOnlyProvider());

        // Assert
        Assert.Equal(2, result.Notifications.Count(notification => notification.Code == "CREDENTIAL_KEY_REQUIRED"));
    }

    [Fact]
    public void Given_NewApiKey_When_Updating_Should_KeepOtherCredentialsAndReturnNewInstance()
    {
        // Arrange
        var integration = PaymentTestData.Integration(credentialKeys: new HashSet<string> { PaymentTestData.API_KEY, PaymentTestData.WEBHOOK_SECRET });
        var changes = Credentials((PaymentTestData.API_KEY, "abc_prod_nova-chave"));
        var now = DateTimeValueObject.Build(PaymentTestData.NOW.Value.AddHours(1));
        var correlationId = IdValueObject.Build();

        // Act
        var result = integration.Update(null, null, changes, now, correlationId);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(new HashSet<string> { PaymentTestData.API_KEY, PaymentTestData.WEBHOOK_SECRET }, result.Output!.CredentialKeys);
        Assert.Equal(integration.Name.Value, result.Output.Name.Value);
        Assert.Equal(now.Value, result.Output.UpdatedAt.Value);
        Assert.Equal(correlationId.Value, result.Output.UpdatedCorrelationId.Value);
        Assert.Equal(PaymentTestData.NOW.Value, integration.UpdatedAt.Value);
    }

    [Fact]
    public void Given_NullOptionalCredential_When_Updating_Should_RemoveIt()
    {
        // Arrange
        var integration = PaymentTestData.Integration(credentialKeys: new HashSet<string> { PaymentTestData.API_KEY, PaymentTestData.WEBHOOK_SECRET });
        var changes = new Dictionary<string, CredentialValueObject?> { [PaymentTestData.WEBHOOK_SECRET] = null };

        // Act
        var result = integration.Update(null, null, changes, PaymentTestData.NOW, PaymentTestData.CORRELATION_ID);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal([PaymentTestData.API_KEY], result.Output!.CredentialKeys);
    }

    [Fact]
    public void Given_NullRequiredCredential_When_Updating_Should_ReturnCredentialKeyRequired()
    {
        // Arrange
        var integration = PaymentTestData.Integration();
        var changes = new Dictionary<string, CredentialValueObject?> { [PaymentTestData.API_KEY] = null };

        // Act
        var result = integration.Update(null, null, changes, PaymentTestData.NOW, PaymentTestData.CORRELATION_ID);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_REQUIRED");
    }

    [Fact]
    public void Given_InactiveStatusAndNewName_When_Updating_Should_ChangeBoth()
    {
        // Arrange
        var integration = PaymentTestData.Integration();

        // Act
        var result = integration.Update("AbacatePay - reserva", IntegrationStatus.INACTIVE, new Dictionary<string, CredentialValueObject?>(), PaymentTestData.NOW, PaymentTestData.CORRELATION_ID);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationStatus.INACTIVE, result.Output!.Status);
        Assert.Equal("AbacatePay - reserva", result.Output.Name.Value);
        Assert.Equal(integration.Id.Value, result.Output.Id.Value);
    }

    [Fact]
    public void Given_UnknownKeyOnUpdate_When_Updating_Should_ReturnCredentialKeyUnknown()
    {
        // Arrange
        var integration = PaymentTestData.Integration();
        var changes = Credentials(("SENHA", "123456"));

        // Act
        var result = integration.Update(null, null, changes, PaymentTestData.NOW, PaymentTestData.CORRELATION_ID);

        // Assert
        Assert.Contains(result.Notifications, notification => notification.Code == "CREDENTIAL_KEY_UNKNOWN");
    }
}
