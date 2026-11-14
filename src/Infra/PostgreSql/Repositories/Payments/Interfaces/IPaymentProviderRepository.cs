using Infra.PostgreSql.Repositories.Payments.Models;

namespace Infra.PostgreSql.Repositories.Payments.Interfaces;

public interface IPaymentProviderRepository
{
    /// <summary>Catálogo inteiro, inclusive os fornecedores desligados, com métodos e chaves de credencial.</summary>
    public Task<IReadOnlyList<PaymentProviderModel>> GetAllAsync(CancellationToken cancellationToken = default);
}
