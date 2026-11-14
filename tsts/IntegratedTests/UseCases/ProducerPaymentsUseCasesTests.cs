using Application.UseCases.Base.Interfaces;
using Application.UseCases.Base.Payments.Outputs;
using Application.UseCases.CreateProducerIntegration.Inputs;
using Application.UseCases.GetProducerIntegrations.Inputs;
using Application.UseCases.GetProducerIntegrations.Outputs;
using Application.UseCases.RankPaymentIntegrations.Inputs;
using Application.UseCases.RankPaymentIntegrations.Outputs;
using Application.UseCases.ReplacePaymentRoute.Inputs;
using Application.UseCases.ReplacePaymentRoute.Outputs;
using Application.UseCases.UpdateProducerIntegration.Inputs;
using IntegratedTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace IntegratedTests.UseCases;

[Collection(PostgreSqlCollection.NAME)]
public sealed class ProducerPaymentsUseCasesTests
{
    private const string SELECT_ENCRYPTED_VALUES_SQL = """
        SELECT string_agg(encode(encrypted_value, 'escape'), '|')
        FROM payments.producer_integration_credential
        WHERE id_producer_integration = @id_producer_integration;
        """;

    private readonly PostgreSqlFixture _fixture;

    public ProducerPaymentsUseCasesTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    // Um escopo por chamada, como uma requisição: cada caso de uso tem o próprio Unit of Work.
    private async Task<TOutput> ExecuteAsync<TInput, TOutput>(TInput input, bool expectSuccess = true)
    {
        using var scope = _fixture.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IUseCase<TInput, TOutput>>()
            .ExecuteAsync(input, ProducerTestData.NewAuditableInfo());

        Assert.Equal(expectSuccess, result.IsSuccess);

        return result.Output!;
    }

    private async Task<string[]> ExecuteExpectingErrorAsync<TInput, TOutput>(TInput input)
    {
        using var scope = _fixture.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IUseCase<TInput, TOutput>>()
            .ExecuteAsync(input, ProducerTestData.NewAuditableInfo());

        Assert.True(result.IsError);

        return result.Notifications.Select(notification => notification.Code).ToArray();
    }

    private Task<ProducerIntegrationOutput> CreateIntegrationAsync(string slug, string providerCode, string name, Dictionary<string, string?> credentials)
        => ExecuteAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(
            CreateProducerIntegrationUseCaseInput.Create(slug, providerCode, name, credentials));

    private Task<ProducerIntegrationOutput> CreateAbacatePayAsync(string slug, string name, string apiKey = "abc_dev_chave-secreta-a91f")
        => CreateIntegrationAsync(slug, PaymentIntegratedTestData.ABACATEPAY, name, new() { [PaymentIntegratedTestData.API_KEY] = apiKey });

    private Task<ReplacePaymentRouteUseCaseOutput> ReplaceRouteAsync(string slug, string paymentMethod, params Guid[] integrationIds)
        => ExecuteAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(
            ReplacePaymentRouteUseCaseInput.Create(slug, paymentMethod, integrationIds.Select(id => (string?)id.ToString()).ToArray()));

    [Fact]
    public async Task Given_Credentials_When_CreatingIntegration_Should_StoreOnlyTheCiphertext()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var providerCode = await PaymentIntegratedTestData.InsertProviderAsync(_fixture, "PIX");
        const string API_KEY_VALUE = "abc_prod_segredo-que-nao-pode-vazar-9f1e";
        const string ACCOUNT_ID_VALUE = "conta-987654321";

        // Act
        var created = await CreateIntegrationAsync(producer.Slug, providerCode, "Conta principal", new()
        {
            [PaymentIntegratedTestData.API_KEY] = API_KEY_VALUE,
            [PaymentIntegratedTestData.ACCOUNT_ID] = ACCOUNT_ID_VALUE,
        });

        // Assert
        var stored = await _fixture.ScalarAsync<string>(
            SELECT_ENCRYPTED_VALUES_SQL,
            new NpgsqlParameter("@id_producer_integration", NpgsqlDbType.Uuid) { Value = created.Id });

        Assert.DoesNotContain(API_KEY_VALUE, stored);
        Assert.DoesNotContain(ACCOUNT_ID_VALUE, stored);

        // Ida e volta: a leitura decifra a chave não secreta e mostra só o final da secreta.
        var integrations = await ExecuteAsync<GetProducerIntegrationsUseCaseInput, GetProducerIntegrationsUseCaseOutput>(GetProducerIntegrationsUseCaseInput.Create(producer.Slug));
        var integration = Assert.Single(integrations.Integrations);
        Assert.Equal(ACCOUNT_ID_VALUE, Assert.Single(integration.Credentials, credential => credential.Key == PaymentIntegratedTestData.ACCOUNT_ID).Value);
        Assert.Equal("9f1e", Assert.Single(integration.Credentials, credential => credential.Key == PaymentIntegratedTestData.API_KEY).ValueHint);
    }

    [Fact]
    public async Task Given_CiphertextCopiedToAnotherIntegration_When_Reading_Should_NotDecrypt()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var providerCode = await PaymentIntegratedTestData.InsertProviderAsync(_fixture, "PIX");
        var first = await CreateIntegrationAsync(producer.Slug, providerCode, "Primeira", new() { ["API_KEY"] = "abc_dev_1111", ["ACCOUNT_ID"] = "conta-1" });
        var second = await CreateIntegrationAsync(producer.Slug, providerCode, "Segunda", new() { ["API_KEY"] = "abc_dev_2222", ["ACCOUNT_ID"] = "conta-2" });

        await _fixture.ExecuteAsync(
            """
            UPDATE payments.producer_integration_credential
            SET encrypted_value = (SELECT encrypted_value FROM payments.producer_integration_credential WHERE id_producer_integration = @first AND credential_key = 'ACCOUNT_ID')
            WHERE id_producer_integration = @second AND credential_key = 'ACCOUNT_ID';
            """,
            new NpgsqlParameter("@first", NpgsqlDbType.Uuid) { Value = first.Id },
            new NpgsqlParameter("@second", NpgsqlDbType.Uuid) { Value = second.Id });

        // Act & Assert
        using var scope = _fixture.Services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<GetProducerIntegrationsUseCaseInput, GetProducerIntegrationsUseCaseOutput>>();

        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => useCase.ExecuteAsync(GetProducerIntegrationsUseCaseInput.Create(producer.Slug), ProducerTestData.NewAuditableInfo()));
    }

    [Fact]
    public async Task Given_NameAlreadyUsed_When_CreatingIntegration_Should_RejectOnlyInTheSameProducer()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var otherProducer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        await CreateAbacatePayAsync(producer.Slug, "Conta principal");

        // Act
        var codes = await ExecuteExpectingErrorAsync<CreateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(
            CreateProducerIntegrationUseCaseInput.Create(producer.Slug, PaymentIntegratedTestData.ABACATEPAY, "Conta  principal", new Dictionary<string, string?> { ["API_KEY"] = "abc_dev_outra" }));
        var otherProducerIntegration = await CreateAbacatePayAsync(otherProducer.Slug, "Conta principal");

        // Assert
        Assert.Contains("INTEGRATION_NAME_ALREADY_EXISTS", codes);
        Assert.Equal("Conta principal", otherProducerIntegration.Name);
    }

    [Fact]
    public async Task Given_IntegrationOfAnotherProducer_When_ReplacingRoute_Should_ReturnIntegrationNotFound()
    {
        // Arrange
        var producerX = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var producerY = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integrationOfY = await CreateAbacatePayAsync(producerY.Slug, "AbacatePay Y");

        // Act
        var codes = await ExecuteExpectingErrorAsync<ReplacePaymentRouteUseCaseInput, ReplacePaymentRouteUseCaseOutput>(
            ReplacePaymentRouteUseCaseInput.Create(producerX.Slug, "PIX", [integrationOfY.Id.ToString()]));

        // Assert
        Assert.Contains("INTEGRATION_NOT_FOUND", codes);
    }

    [Fact]
    public async Task Given_IntegrationOfAnotherProducer_When_UpdatingIt_Should_ReturnIntegrationNotFound()
    {
        // Arrange
        var producerX = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var producerY = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integrationOfY = await CreateAbacatePayAsync(producerY.Slug, "AbacatePay Y");

        // Act
        var codes = await ExecuteExpectingErrorAsync<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(
            UpdateProducerIntegrationUseCaseInput.Create(producerX.Slug, integrationOfY.Id.ToString(), null, "INACTIVE", null));

        // Assert
        Assert.Contains("INTEGRATION_NOT_FOUND", codes);
    }

    [Fact]
    public async Task Given_PixRouteWithFallback_When_PrincipalIsInactivated_Should_RankTheFallback()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var principal = await CreateAbacatePayAsync(producer.Slug, "Conta principal");
        var backup = await CreateAbacatePayAsync(producer.Slug, "Conta reserva");
        await ReplaceRouteAsync(producer.Slug, "PIX", principal.Id, backup.Id);

        var rankingInput = RankPaymentIntegrationsUseCaseInput.Create(producer.Id, "PIX");
        var before = await ExecuteAsync<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>(rankingInput);

        // Act
        await ExecuteAsync<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(
            UpdateProducerIntegrationUseCaseInput.Create(producer.Slug, principal.Id.ToString(), null, "INACTIVE", null));
        var after = await ExecuteAsync<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>(rankingInput);

        // Assert
        Assert.Equal([principal.Id, backup.Id], before.Integrations.Select(integration => integration.IntegrationId));
        Assert.Equal([backup.Id], after.Integrations.Select(integration => integration.IntegrationId));
    }

    [Fact]
    public async Task Given_ProviderDisabledByNtix_When_Ranking_Should_DiscardItForEveryProducer()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var providerCode = await PaymentIntegratedTestData.InsertProviderAsync(_fixture, "PIX");
        var disabled = await CreateIntegrationAsync(producer.Slug, providerCode, "Será desligado", new() { ["API_KEY"] = "abc_dev_1111", ["ACCOUNT_ID"] = "conta-1" });
        var abacatePay = await CreateAbacatePayAsync(producer.Slug, "AbacatePay");
        await ReplaceRouteAsync(producer.Slug, "PIX", disabled.Id, abacatePay.Id);

        // Act
        await _fixture.ExecuteAsync(
            "UPDATE payments.provider SET status = 'DISABLED' WHERE code = @code;",
            new NpgsqlParameter("@code", NpgsqlDbType.Varchar) { Value = providerCode });
        var ranking = await ExecuteAsync<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>(RankPaymentIntegrationsUseCaseInput.Create(producer.Id, "PIX"));

        // Assert
        var ranked = Assert.Single(ranking.Integrations);
        Assert.Equal(abacatePay.Id, ranked.IntegrationId);
        Assert.Equal(2, ranked.Priority);
    }

    [Fact]
    public async Task Given_MethodWithoutRoute_When_Ranking_Should_ReturnPaymentMethodUnavailable()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);

        // Act
        var codes = await ExecuteExpectingErrorAsync<RankPaymentIntegrationsUseCaseInput, RankPaymentIntegrationsUseCaseOutput>(RankPaymentIntegrationsUseCaseInput.Create(producer.Id, "BOLETO"));

        // Assert
        Assert.Equal(["PAYMENT_METHOD_UNAVAILABLE"], codes);
    }

    [Fact]
    public async Task Given_NewCredentialValue_When_Updating_Should_ReplaceTheCiphertextAndHint()
    {
        // Arrange
        var producer = await PaymentIntegratedTestData.InsertProducerAsync(_fixture);
        var integration = await CreateAbacatePayAsync(producer.Slug, "AbacatePay", "abc_dev_antiga-1111");

        // Act
        var updated = await ExecuteAsync<UpdateProducerIntegrationUseCaseInput, ProducerIntegrationOutput>(
            UpdateProducerIntegrationUseCaseInput.Create(producer.Slug, integration.Id.ToString(), null, null, new Dictionary<string, string?> { ["API_KEY"] = "abc_prod_nova-2222" }));

        // Assert
        Assert.Equal("2222", Assert.Single(updated.Credentials).ValueHint);
        var stored = await _fixture.ScalarAsync<string>(SELECT_ENCRYPTED_VALUES_SQL, new NpgsqlParameter("@id_producer_integration", NpgsqlDbType.Uuid) { Value = integration.Id });
        Assert.DoesNotContain("abc_prod_nova-2222", stored);
        Assert.DoesNotContain("abc_dev_antiga", stored);
    }
}
