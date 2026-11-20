namespace Domain.BoundedContexts.EventContext.Events;

/// <summary>Evento publicado (REQ10, RN11). Gravado na outbox na mesma transação; o consumidor vem com o requisito que precisar dele.</summary>
public sealed record EventPublishedEvent(Guid EventId, Guid ProducerId, string Slug, DateTime OccurredAt)
{
    public const string EVENT_TYPE = "EventPublished";
}
