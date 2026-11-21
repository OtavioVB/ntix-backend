namespace Domain.BoundedContexts.EventContext.Enums;

/// <summary>
/// Situação do evento. Nasce <see cref="DRAFT"/> (REQ9) e só a publicação (REQ10) o leva a <see cref="PUBLISHED"/>.
/// <see cref="ENDED"/> (REQ14) não é gravada: é a situação exposta de todo evento cujo término já passou
/// (<see cref="Entities.Event.StatusAt"/>).
/// </summary>
public enum EventStatus
{
    DRAFT = 1,
    PUBLISHED = 2,
    ENDED = 3,
}
