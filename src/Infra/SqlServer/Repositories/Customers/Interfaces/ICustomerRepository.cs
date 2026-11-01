using Infra.SqlServer.Repositories.Customers.Enums;
using Infra.SqlServer.Repositories.Customers.Models;

namespace Infra.SqlServer.Repositories.Customers.Interfaces;

public interface ICustomerRepository
{
    public Task<bool> ExistsByEmailOrDocumentAsync(string email, string document, CancellationToken cancellationToken = default);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    public Task<CustomerCreationResult> CreateAsync(CustomerModel customer, CancellationToken cancellationToken = default);

    public Task<CustomerCredentialsModel?> FindCredentialsByEmailAsync(string email, Guid producerId, CancellationToken cancellationToken = default);

    public Task<CustomerCredentialsModel?> FindCredentialsByDocumentAsync(string document, Guid producerId, CancellationToken cancellationToken = default);
}
