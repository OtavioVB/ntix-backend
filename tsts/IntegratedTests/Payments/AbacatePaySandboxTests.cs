using Application.UseCases.Base.Payments;
using Infra.Http.Payments;
using Infra.Http.Payments.Interfaces;
using Infra.Http.Payments.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegratedTests.Payments;

/// <summary>
/// <see cref="FactAttribute"/> que só roda com a chave de teste do AbacatePay em <c>ABACATEPAY_SANDBOX_API_KEY</c>. A esteira não define a
/// variável e nunca chama o fornecedor (REQ7); quem muda o adaptador roda estes testes na máquina, com uma chave <c>abc_dev_...</c>.
/// </summary>
public sealed class SandboxFactAttribute : FactAttribute
{
    public const string API_KEY_VARIABLE = "ABACATEPAY_SANDBOX_API_KEY";

    public SandboxFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(API_KEY_VARIABLE)))
            Skip = $"Defina {API_KEY_VARIABLE} com uma chave de desenvolvimento (abc_dev_...) para rodar o contrato com o sandbox do AbacatePay.";
    }
}

/// <summary>
/// Contrato do adaptador com o sandbox real do AbacatePay (REQ7). Cada teste é uma regra que o fornecedor impõe e que o sandbox
/// confirmou: o campo <c>customer.cellphone</c> é obrigatório (vazio é aceito, ausente ou nulo dão 422), o valor mínimo é R$ 1,00 e
/// a chave inválida é recusada com 401.
/// </summary>
public sealed class AbacatePaySandboxTests
{
    private static string ApiKey => Environment.GetEnvironmentVariable(SandboxFactAttribute.API_KEY_VARIABLE)!;

    private static IPixChargeClient BuildClient()
    {
        var provider = new ServiceCollection().AddPixChargeClientsConfiguration(new ConfigurationBuilder().Build()).BuildServiceProvider();

        return provider.GetServices<IPixChargeClient>().Single();
    }

    private static PixChargeRequestModel Request(long amountCents = 12000, string cellphone = "(81) 99999-0000", string description = "Pedido NX7K2QH9MP - Festival Horizonte")
        => new(
            PaymentId: Guid.NewGuid(),
            OrderCode: "NX7K2QH9MP",
            AmountCents: amountCents,
            ExpiresInSeconds: 780,
            Description: description,
            CustomerName: "Marina Souza",
            CustomerEmail: "marina@exemplo.com",
            CustomerTaxId: "52998224725",
            CustomerCellphone: cellphone);

    [SandboxFact]
    public async Task Given_CustomerWithCellphone_When_Charging_Should_CreateThePix()
    {
        // Arrange & Act
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request());

        // Assert
        Assert.Equal(PixChargeOutcome.Created, result.Outcome);
        Assert.StartsWith("pix_char_", result.Reference);
        Assert.False(string.IsNullOrWhiteSpace(result.CopyPasteCode));
        Assert.StartsWith("data:image/png;base64,", result.QrCodeImage);
    }

    [SandboxFact]
    public async Task Given_CustomerWithoutCellphone_When_Charging_Should_CreateThePixWithAnEmptyCellphone()
    {
        // Arrange & Act: o caso do cliente que não informou celular no cadastro (era o 422 que derrubava todas as integrações).
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request(cellphone: string.Empty));

        // Assert
        Assert.Equal(PixChargeOutcome.Created, result.Outcome);
        Assert.StartsWith("pix_char_", result.Reference);
    }

    [SandboxFact]
    public async Task Given_TitleWithAccentsAndEmoji_When_Charging_Should_CreateThePixWithTheSanitizedDescription()
    {
        // Arrange
        var description = PixChargeDescription.Build("NX7K2QH9MP", "Noite de Vinil \U0001F3B6 ao vivo: música e coração");

        // Act
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request(description: description));

        // Assert
        Assert.Equal(PixChargeOutcome.Created, result.Outcome);
    }

    [SandboxFact]
    public async Task Given_TheMinimumAmount_When_Charging_Should_CreateThePix()
    {
        // Arrange & Act
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request(amountCents: 100));

        // Assert
        Assert.Equal(PixChargeOutcome.Created, result.Outcome);
    }

    [SandboxFact]
    public async Task Given_AmountBelowTheMinimum_When_Charging_Should_RefuseWithTheProviderReason()
    {
        // Arrange & Act
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request(amountCents: 99));

        // Assert
        Assert.Equal(PixChargeOutcome.ProviderError, result.Outcome);
        Assert.Equal("http_4xx", result.FailureReason);
        Assert.Contains("100", result.FailureDetail);
    }

    [SandboxFact]
    public async Task Given_InvalidTaxId_When_Charging_Should_RefuseWithTheProviderReason()
    {
        // Arrange & Act
        var result = await BuildClient().CreatePixChargeAsync(ApiKey, Request() with { CustomerTaxId = "11111111111" });

        // Assert
        Assert.Equal("http_4xx", result.FailureReason);
        Assert.Contains("taxId", result.FailureDetail);
    }

    [SandboxFact]
    public async Task Given_InvalidApiKey_When_Charging_Should_RefuseAsUnauthorizedWithoutDetail()
    {
        // Arrange & Act
        var result = await BuildClient().CreatePixChargeAsync("abc_dev_chave-que-nao-existe", Request());

        // Assert
        Assert.Equal(PixChargeOutcome.ProviderError, result.Outcome);
        Assert.Equal("http_401", result.FailureReason);
        Assert.Null(result.FailureDetail);
    }
}
