namespace Application.UseCases.GetEventDailySales.Outputs;

/// <summary>Vendas por dia do evento (REQ14), com todos os dias do período, inclusive os sem venda. Datas <c>yyyy-MM-dd</c>.</summary>
public sealed record GetEventDailySalesUseCaseOutput(string From, string To, IReadOnlyList<DailySalesOutput> Days);

/// <summary>Vendas efetivadas de um dia: ingressos, vendas brutas em centavos e pedidos pagos.</summary>
public sealed record DailySalesOutput(string Date, int SoldQuantity, long GrossCents, int PaidOrders);
