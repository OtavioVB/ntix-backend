namespace Domain.BoundedContexts.ProducerContext.Events;

/// <summary>
/// Produtora cadastrada. O consumidor (requisito próprio) pede a Contas a carteira interna, WALLET BLOCKED com a chave de
/// idempotência <see cref="WalletIdempotencyKey"/>, e cria a PRODUCER_WALLET.
/// </summary>
public sealed record ProducerRegisteredEvent(Guid ProducerId, DateTime OccurredAt)
{
    public const string EVENT_TYPE = "ProducerRegistered";

    public string WalletIdempotencyKey => $"producer:{ProducerId}";
}
