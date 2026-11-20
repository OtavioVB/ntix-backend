namespace Infra.PostgreSql.Repositories.Orders.Enums;

public enum OrderCreationResult
{
    Created = 1,

    /// <summary>O código sorteado já existe (<c>ux_orders_code</c>); a transação segue válida e o chamador sorteia outro.</summary>
    CodeAlreadyExists = 2,
}
