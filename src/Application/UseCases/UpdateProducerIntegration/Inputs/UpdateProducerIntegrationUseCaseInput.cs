using Domain.CrossCutting.MethodResultContext;
using Domain.ValueObjects;

namespace Application.UseCases.UpdateProducerIntegration.Inputs;

public sealed record UpdateProducerIntegrationUseCaseInput
{
    /// <summary>Slug da produtora na rota, já autorizado contra o do token.</summary>
    public SlugValueObject ProducerSlug { get; set; }

    /// <summary>Id da integração na rota. Fora do formato é tratado como inexistente.</summary>
    public string? IntegrationId { get; set; }

    /// <summary>Nulo mantém o nome atual.</summary>
    public IntegrationNameValueObject? Name { get; set; }

    /// <summary>Nulo mantém a situação atual.</summary>
    public IntegrationStatusValueObject? Status { get; set; }

    /// <summary>Só as chaves enviadas mudam; valor nulo remove a chave, se não for obrigatória.</summary>
    public IReadOnlyDictionary<string, CredentialValueObject?> Credentials { get; set; }

    private UpdateProducerIntegrationUseCaseInput(SlugValueObject producerSlug, string? integrationId, IntegrationNameValueObject? name, IntegrationStatusValueObject? status, IReadOnlyDictionary<string, CredentialValueObject?> credentials)
    {
        ProducerSlug = producerSlug;
        IntegrationId = integrationId;
        Name = name;
        Status = status;
        Credentials = credentials;
    }

    public static UpdateProducerIntegrationUseCaseInput Create(string? producerSlug, string? integrationId, string? name, string? status, IReadOnlyDictionary<string, string?>? credentials)
        => new(
            producerSlug: SlugValueObject.Parse(producerSlug),
            integrationId: integrationId,
            name: name is null ? null : IntegrationNameValueObject.Build(name),
            status: status is null ? null : IntegrationStatusValueObject.Build(status),
            credentials: (credentials ?? new Dictionary<string, string?>())
                .ToDictionary(
                    credential => credential.Key,
                    credential => credential.Value is null ? null : (CredentialValueObject?)CredentialValueObject.Build(credential.Value),
                    StringComparer.Ordinal));

    public MethodResult GetInputValidation()
        => MethodResult.Factory(
            ProducerSlug.MethodResult,
            Name?.MethodResult ?? MethodResult.Success(),
            Status?.MethodResult ?? MethodResult.Success());
}
