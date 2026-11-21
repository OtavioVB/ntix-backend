namespace Domain.BoundedContexts.OrderContext.Enums;

/// <summary>
/// Período dos ingressos do comprador (REQ13, RN5). <see cref="UPCOMING"/>: evento que ainda não terminou, por início crescente.
/// <see cref="PAST"/>: evento já terminado, por início decrescente.
/// </summary>
public enum TicketPeriod
{
    UPCOMING = 1,
    PAST = 2,
}
