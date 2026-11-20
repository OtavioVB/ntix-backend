namespace Domain.BoundedContexts.EventContext.Enums;

/// <summary>
/// Situação do evento. Nasce <see cref="DRAFT"/> (REQ9) e só a publicação (REQ10) o leva a <see cref="PUBLISHED"/>.
/// O fim do evento é derivado de <c>ends_at</c>, não é uma situação gravada.
/// </summary>
public enum EventStatus
{
    DRAFT = 1,
    PUBLISHED = 2,
}
