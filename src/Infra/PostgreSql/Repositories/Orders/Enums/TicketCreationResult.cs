namespace Infra.PostgreSql.Repositories.Orders.Enums;

public enum TicketCreationResult
{
    Created = 1,

    /// <summary>Um código sorteado já existe (RN7). A transação segue viva: o caso de uso sorteia outros códigos e tenta de novo.</summary>
    CodeAlreadyExists = 2,
}
