namespace WebApi.Controllers.Payloads;

/// <summary>Corpo do pagamento do pedido (REQ7). O valor não vem do comprador: é o total do pedido.</summary>
public sealed record CreateOrderPaymentV1Request(string? Method);
