namespace Domain.BoundedContexts.EventContext.Enums;

/// <summary>Situação do lote informada pelo produtor (REQ10, RN8). Lote não é excluído: é pausado com <see cref="INACTIVE"/>.</summary>
public enum BatchStatus
{
    ACTIVE = 1,
    INACTIVE = 2,
}
