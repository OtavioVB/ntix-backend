namespace Domain.BoundedContexts.ProducerContext.Enums;

/// <summary>Situação do refresh token de uma sessão (REQ5). Não é gravada: deriva das datas e da substituição.</summary>
public enum ProducerUserSessionState
{
    /// <summary>Pode renovar a sessão.</summary>
    ACTIVE = 1,

    /// <summary>Já foi trocado por outro na renovação. Reapresentá-lo é reuso e revoga a família inteira (RN7).</summary>
    REPLACED = 2,

    /// <summary>Revogado pelo logout ou pela revogação da família.</summary>
    REVOKED = 3,

    /// <summary>Passou 7 dias sem renovação.</summary>
    EXPIRED = 4,
}
