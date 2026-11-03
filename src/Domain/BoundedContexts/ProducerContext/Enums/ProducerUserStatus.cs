namespace Domain.BoundedContexts.ProducerContext.Enums;

public enum ProducerUserStatus
{
    /// <summary>Cadastrado, com o e-mail ainda não verificado: não acessa a plataforma.</summary>
    PENDING = 1,
    ACTIVE = 2,
    BLOCKED = 3,
}
