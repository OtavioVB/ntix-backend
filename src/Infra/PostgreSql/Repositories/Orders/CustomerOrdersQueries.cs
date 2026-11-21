namespace Infra.PostgreSql.Repositories.Orders;

/// <summary>
/// Fragmentos fixos das listas do comprador (REQ13). Só estes textos entram no SQL, escolhidos pelo filtro já validado:
/// nenhum valor do cliente vira texto de consulta.
/// </summary>
internal static class CustomerOrdersQueries
{
    // Efetivo (RN3): PENDING_PAYMENT com a reserva vencida conta como EXPIRED, sem rotina em segundo plano (REQ6, RN12).
    public const string FILTER_ALL = "";
    public const string FILTER_PAID = " AND o.status = 'PAID'";
    public const string FILTER_PENDING = " AND o.status = 'PENDING_PAYMENT' AND o.expires_at > @now";
    public const string FILTER_EXPIRED = " AND (o.status = 'EXPIRED' OR (o.status = 'PENDING_PAYMENT' AND o.expires_at <= @now))";

    public const string FROM_ORDERS = """
        FROM orders.orders AS o
        JOIN events.event AS e ON e.id_event = o.id_event
        WHERE o.id_customer = @id_customer
        """;

    public const string SELECT_ORDERS = """
        SELECT o.code, o.status, o.expires_at, o.created_at, o.paid_at, o.total_amount,
               (SELECT COUNT(*)::int FROM orders.ticket t WHERE t.id_order = o.id_order) AS ticket_count,
               o.id_producer, e.slug, e.title, e.starts_at, e.city, e.state, e.image_url
        """;

    public const string ORDER_BY_CREATED = " ORDER BY o.created_at DESC, o.code DESC LIMIT @page_size OFFSET @offset;";

    public const string COUNT_ORDERS = "SELECT COUNT(*)::int ";

    public static string FilterFor(string? filterKey) => filterKey switch
    {
        "PAID" => FILTER_PAID,
        "PENDING_PAYMENT" => FILTER_PENDING,
        "EXPIRED" => FILTER_EXPIRED,
        _ => FILTER_ALL,
    };
}
