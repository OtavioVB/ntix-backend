using Domain.BoundedContexts.OrderContext.Entities;
using Domain.BoundedContexts.OrderContext.Events;

namespace Domain.BoundedContexts.OrderContext.Services;

/// <summary>Resultado da confirmação: o pedido pago, os ingressos emitidos e o evento. <see cref="PaidAfterExpiration"/> sinaliza RN10.</summary>
public sealed record OrderPaymentConfirmation(Order Order, IReadOnlyList<Ticket> Tickets, OrderPaidEvent Event, bool PaidAfterExpiration);
