using Domain.BoundedContexts.OrderContext.Entities;
using Domain.CrossCutting.MethodResultContext;
using Domain.CrossCutting.NotificationContext;
using Domain.CrossCutting.NotificationContext.Interfaces;
using Domain.ValueObjects;

namespace Application.UseCases.CreateOrder.Inputs;

public sealed record CreateOrderUseCaseInput
{
    /// <summary>Id do cliente do access token (<c>sub</c>). Vazio quando o claim não é um GUID.</summary>
    public Guid CustomerId { get; set; }

    public string EventSlug { get; set; }

    public SalesChannelValueObject SalesChannel { get; set; }

    public IReadOnlyList<OrderItemRequest> Items { get; set; }

    private CreateOrderUseCaseInput(Guid customerId, string eventSlug, SalesChannelValueObject salesChannel, IReadOnlyList<OrderItemRequest> items)
    {
        CustomerId = customerId;
        EventSlug = eventSlug;
        SalesChannel = salesChannel;
        Items = items;
    }

    public const string ORDER_CUSTOMER_REQUIRED_NOTIFICATION_CODE = "ORDER_CUSTOMER_REQUIRED";
    private const string ORDER_CUSTOMER_REQUIRED_NOTIFICATION_MESSAGE = "O pedido precisa de um cliente autenticado.";

    public const string ORDER_EVENT_REQUIRED_NOTIFICATION_CODE = "ORDER_EVENT_REQUIRED";
    private const string ORDER_EVENT_REQUIRED_NOTIFICATION_MESSAGE = "Informe o evento do pedido.";

    public const string ORDER_ITEMS_REQUIRED_NOTIFICATION_CODE = "ORDER_ITEMS_REQUIRED";
    private const string ORDER_ITEMS_REQUIRED_NOTIFICATION_MESSAGE = "O pedido precisa de ao menos um item.";

    public const string ORDER_ITEM_DUPLICATED_NOTIFICATION_CODE = "ORDER_ITEM_DUPLICATED";
    private const string ORDER_ITEM_DUPLICATED_NOTIFICATION_MESSAGE = "Cada lote só pode aparecer uma vez no pedido.";

    /// <param name="items">Lote e quantidade de cada item; o id que não é um GUID vira um lote inexistente, respondido como tal.</param>
    public static CreateOrderUseCaseInput Create(string? customerId, string? eventSlug, string? salesChannel, IEnumerable<(string? BatchId, int? Quantity)>? items)
        => new(
            customerId: Guid.TryParse(customerId, out var parsed) ? parsed : Guid.Empty,
            eventSlug: (eventSlug ?? string.Empty).Trim(),
            salesChannel: SalesChannelValueObject.Build(salesChannel),
            items: (items ?? [])
                .Select(item => new OrderItemRequest(
                    BatchId: Guid.TryParse(item.BatchId, out var batchId) ? batchId : Guid.Empty,
                    Quantity: OrderItemQuantityValueObject.Build(item.Quantity)))
                .ToArray());

    public MethodResult GetInputValidation()
    {
        var notifications = new List<INotification>();

        if (CustomerId == Guid.Empty)
            notifications.Add(Notification.Error(ORDER_CUSTOMER_REQUIRED_NOTIFICATION_CODE, ORDER_CUSTOMER_REQUIRED_NOTIFICATION_MESSAGE));

        if (EventSlug.Length == 0)
            notifications.Add(Notification.Error(ORDER_EVENT_REQUIRED_NOTIFICATION_CODE, ORDER_EVENT_REQUIRED_NOTIFICATION_MESSAGE));

        notifications.AddRange(SalesChannel.MethodResult.Notifications);

        if (Items.Count == 0)
            notifications.Add(Notification.Error(ORDER_ITEMS_REQUIRED_NOTIFICATION_CODE, ORDER_ITEMS_REQUIRED_NOTIFICATION_MESSAGE));

        // Uma notificação por regra violada, mesmo que vários itens a violem.
        notifications.AddRange(Items.SelectMany(item => item.Quantity.MethodResult.Notifications).DistinctBy(notification => notification.Code));

        if (Items.GroupBy(item => item.BatchId).Any(group => group.Count() > 1))
            notifications.Add(Notification.Error(ORDER_ITEM_DUPLICATED_NOTIFICATION_CODE, ORDER_ITEM_DUPLICATED_NOTIFICATION_MESSAGE));

        return notifications.Count == 0 ? MethodResult.Success() : MethodResult.Error([.. notifications]);
    }
}
