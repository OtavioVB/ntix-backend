namespace Application.UseCases.Base.Orders.Outputs;

/// <summary>QR Code e Pix copia e cola da cobrança. Só aparecem com a cobrança aguardando pagamento e dentro da validade (REQ7).</summary>
public sealed record PixOutput(string CopyPasteCode, string QrCodeImage);

/// <summary>Cobrança do pedido (REQ7). <c>Status</c> já é o efetivo: Pix vencido aparece como EXPIRED, sem o bloco <c>pix</c>.</summary>
public sealed record OrderPaymentOutput(Guid Id, string Method, string Status, long AmountCents, DateTime ExpiresAt, PixOutput? Pix);

public sealed record PaymentMethodOutput(string Method);

public sealed record PaymentMethodsOutput(IReadOnlyList<PaymentMethodOutput> Methods);

/// <param name="Reused">Verdadeiro quando o Pix pendente já existia e foi devolvido, sem nova cobrança (RN4): o controller responde 200 em vez de 201.</param>
public sealed record CreateOrderPaymentOutput(OrderPaymentOutput Payment, bool Reused);
