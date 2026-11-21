using Domain.BoundedContexts.OrderContext.Enums;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;

namespace Domain.ValueObjects;

/// <summary>
/// Filtros das listas do comprador (REQ13). O status de pedido aceita os da API (RN3, sobre o status efetivo); o período de ingressos
/// é <c>UPCOMING</c> (padrão) ou <c>PAST</c> (RN5). Valor desconhecido recusa a consulta, com o código da lista.
/// </summary>
public readonly struct CustomerListFiltersValueObject
{
    public const string ORDER_FILTER_INVALID_NOTIFICATION_CODE = "ORDER_FILTER_INVALID";
    public const string TICKET_FILTER_INVALID_NOTIFICATION_CODE = "TICKET_FILTER_INVALID";

    public bool IsValid { get; }

    /// <summary>Status efetivo pedido pelo comprador; nulo quando a lista traz todos.</summary>
    public OrderStatus? OrderStatus { get; }

    /// <summary>Período dos ingressos pedido pelo comprador; <see cref="TicketPeriod.UPCOMING"/> quando não informado.</summary>
    public TicketPeriod TicketPeriod { get; }

    public MethodResult MethodResult { get; }

    private CustomerListFiltersValueObject(bool isValid, OrderStatus? orderStatus, TicketPeriod ticketPeriod, MethodResult methodResult)
    {
        IsValid = isValid;
        OrderStatus = orderStatus;
        TicketPeriod = ticketPeriod;
        MethodResult = methodResult;
    }

    public static CustomerListFiltersValueObject BuildOrderFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return new CustomerListFiltersValueObject(isValid: true, orderStatus: null, ticketPeriod: TicketPeriod.UPCOMING, methodResult: MethodResult.Success());

        if (Enum.TryParse<OrderStatus>(status.Trim().ToUpperInvariant(), out var parsed) && Enum.IsDefined(parsed))
            return new CustomerListFiltersValueObject(isValid: true, orderStatus: parsed, ticketPeriod: TicketPeriod.UPCOMING, methodResult: MethodResult.Success());

        var notification = Notification.Error(ORDER_FILTER_INVALID_NOTIFICATION_CODE, "O status do filtro deve ser PENDING_PAYMENT, PAID ou EXPIRED.");

        return new CustomerListFiltersValueObject(isValid: false, orderStatus: null, ticketPeriod: TicketPeriod.UPCOMING, methodResult: MethodResult.Error([notification]));
    }

    public static CustomerListFiltersValueObject BuildTicketFilter(string? when)
    {
        if (string.IsNullOrWhiteSpace(when))
            return new CustomerListFiltersValueObject(isValid: true, orderStatus: null, ticketPeriod: TicketPeriod.UPCOMING, methodResult: MethodResult.Success());

        if (Enum.TryParse<TicketPeriod>(when.Trim().ToUpperInvariant(), out var parsed) && Enum.IsDefined(parsed))
            return new CustomerListFiltersValueObject(isValid: true, orderStatus: null, ticketPeriod: parsed, methodResult: MethodResult.Success());

        var notification = Notification.Error(TICKET_FILTER_INVALID_NOTIFICATION_CODE, "O período do filtro deve ser UPCOMING ou PAST.");

        return new CustomerListFiltersValueObject(isValid: false, orderStatus: null, ticketPeriod: TicketPeriod.UPCOMING, methodResult: MethodResult.Error([notification]));
    }
}
