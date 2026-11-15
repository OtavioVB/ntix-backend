namespace Domain.BoundedContexts.ProducerContext.Enums;

public enum ProducerUserStatus
{
    /// <summary>Cadastrado, com o e-mail ainda não verificado: acessa a plataforma até existir a verificação de e-mail (REQ5, RN2).</summary>
    PENDING = 1,
    ACTIVE = 2,

    /// <summary>Não entra na plataforma nem renova a sessão (REQ5, RN2 e RN8).</summary>
    BLOCKED = 3,
}
