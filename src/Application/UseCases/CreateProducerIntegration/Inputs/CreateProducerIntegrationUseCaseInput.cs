using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.CreateProducerIntegration.Inputs;

public sealed record CreateProducerIntegrationUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Código do fornecedor no catálogo (ex.: ABACATEPAY). Validado contra o catálogo no caso de uso.</summary>
    public string? ProviderCode { get; set; }

    public IntegrationNameValueObject Name { get; set; }

    /// <summary>Credenciais em chave-valor. As chaves são validadas contra o catálogo do fornecedor (RN4).</summary>
    public IReadOnlyDictionary<string, CredentialValueObject?> Credentials { get; set; }

    private CreateProducerIntegrationUseCaseInput(SlugValueObject producerSlug, string? providerCode, IntegrationNameValueObject name, IReadOnlyDictionary<string, CredentialValueObject?> credentials)
    {
        ProducerSlug = producerSlug;
        ProviderCode = providerCode;
        Name = name;
        Credentials = credentials;
    }

    /// <param name="credentials">Valor nulo é tratado como vazio: toda credencial enviada precisa de valor.</param>
    public static CreateProducerIntegrationUseCaseInput Create(string? producerSlug, string? providerCode, IntegrationNameValueObject name, IReadOnlyDictionary<string, string?>? credentials)
        => new(
            producerSlug: SlugValueObject.Parse(producerSlug),
            providerCode: providerCode,
            name: name,
            credentials: (credentials ?? new Dictionary<string, string?>())
                .ToDictionary(credential => credential.Key, credential => (CredentialValueObject?)CredentialValueObject.Build(credential.Value), StringComparer.Ordinal));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(ProducerSlug.MethodResult, Name.MethodResult);
}
