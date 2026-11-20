namespace Domain.BoundedContexts.CustomerContext.Enums;

/// <summary>Situação do refresh token de uma sessão do cliente (REQ12). Não é gravada: deriva das datas e da substituição.</summary>
public enum CustomerSessionState
{
    /// <summary>Pode renovar a sessão.</summary>
    ACTIVE = 1,

    /// <summary>Já foi trocado por outro na renovação. Reapresentá-lo é reuso e revoga a família inteira (RN5).</summary>
    REPLACED = 2,

    /// <summary>Revogado pelo logout ou pela revogação da família.</summary>
    REVOKED = 3,

    /// <summary>Passou da validade sem renovação.</summary>
    EXPIRED = 4,
}
