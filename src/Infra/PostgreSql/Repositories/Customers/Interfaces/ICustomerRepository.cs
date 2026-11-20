using Infra.PostgreSql.Repositories.Customers.Enums;
using Infra.PostgreSql.Repositories.Customers.Models;

namespace Infra.PostgreSql.Repositories.Customers.Interfaces;

public interface ICustomerRepository
{
    public Task<bool> ExistsByEmailOrDocumentAsync(string email, string document, CancellationToken cancellationToken = default);

    public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Cliente pelo e-mail normalizado, consulta exata sobre <c>ux_customer_email</c> (login, REQ12). Nulo quando não existe.</summary>
    public Task<CustomerModel?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Cliente pelo CPF só com dígitos, consulta exata sobre <c>ux_customer_document</c> (login, REQ12). Nulo quando não existe.</summary>
    public Task<CustomerModel?> GetByDocumentAsync(string document, CancellationToken cancellationToken = default);

    public Task<CustomerModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    public Task<CustomerCreationResult> CreateAsync(CustomerModel customer, CancellationToken cancellationToken = default);
}
