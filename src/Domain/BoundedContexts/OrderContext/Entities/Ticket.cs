using Domain.BoundedContexts.OrderContext.Enums;
using Domain.ValueObjects;

namespace Domain.BoundedContexts.OrderContext.Entities;

/// <summary>Ingresso de uma unidade comprada, emitido na confirmação do pagamento (REQ8, RN7). O titular é o nome do comprador.</summary>
public sealed record Ticket(
    IdValueObject Id,
    IdValueObject OrderItemId,
    IdValueObject OrderId,
    IdValueObject CustomerId,
    IdValueObject EventId,
    string Code,
    string HolderName,
    TicketStatus Status,
    DateTime CreatedAt,
    IdValueObject CreatedCorrelationId)
{
    /// <summary>Mesmo ingresso com outro código, usado quando o código sorteado colide no banco (RN7).</summary>
    public Ticket WithNewCode()
        => this with { Code = TicketCodeValueObject.Generate().Value };
}
