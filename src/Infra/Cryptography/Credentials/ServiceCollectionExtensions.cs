using Infra.Cryptography.Base.Configuration;
using Infra.Cryptography.Credentials.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Cryptography.Credentials;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCredentialCipherConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var paymentCredentialConfiguration = configuration.GetRequiredSection("PaymentCredentialConfiguration").Get<PaymentCredentialConfiguration>()!;

        // Falha na subida, e não na primeira cobrança: sem a chave nenhuma credencial é lida nem gravada.
        CredentialCipher.DecodeKey(paymentCredentialConfiguration.EncryptionKey);

        services.AddSingleton(paymentCredentialConfiguration);
        services.AddSingleton<ICredentialCipher, CredentialCipher>();

        return services;
    }
}
