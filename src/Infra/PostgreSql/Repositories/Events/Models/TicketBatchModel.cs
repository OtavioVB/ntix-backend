namespace Infra.PostgreSql.Repositories.Events.Models;

/// <summary>Linha de <c>events.ticket_batch</c>, com tipos primitivos. Preço em reais (<c>NUMERIC(18,2)</c>).</summary>
public sealed record TicketBatchModel(
    Guid Id,
    Guid EventId,
    Guid ProducerId,
    string Name,
    string? Note,
    decimal Price,
    int Quantity,
    DateTime SalesStartsAt,
    DateTime SalesEndsAt,
    string Status,
    DateTime CreatedAt,
    Guid CreatedCorrelationId,
    DateTime UpdatedAt,
    Guid UpdatedCorrelationId);

/// <summary>Ingressos vendidos (pedidos pagos) e reservados (pedidos aguardando pagamento dentro da reserva) de um lote.</summary>
public sealed record TicketBatchSalesModel(int Sold, int Reserved);
