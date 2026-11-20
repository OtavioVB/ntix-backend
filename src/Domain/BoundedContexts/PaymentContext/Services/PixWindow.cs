namespace Domain.BoundedContexts.PaymentContext.Services;

/// <param name="Fits">Falso quando a reserva restante não comporta um Pix com a validade mínima do fornecedor.</param>
/// <param name="ExpiresAt">Fim do Pix: a folga de confirmação antes do fim da reserva.</param>
/// <param name="ExpiresInSeconds">Validade a pedir ao fornecedor, em segundos inteiros.</param>
public sealed record PixWindowResult(bool Fits, DateTime ExpiresAt, int ExpiresInSeconds);

/// <summary>
/// Janela do Pix dentro da reserva do pedido (REQ7, RN5): o Pix termina <c>grace</c> antes do fim da reserva, para um pagamento
/// feito no último segundo ainda encontrar os ingressos reservados quando a confirmação chegar, e precisa ter ao menos a
/// validade mínima do fornecedor.
/// </summary>
public static class PixWindow
{
    public static PixWindowResult Calculate(DateTime orderExpiresAt, TimeSpan grace, TimeSpan providerMinimum, DateTime now)
    {
        var expiresAt = orderExpiresAt - grace;
        var seconds = (int)Math.Floor((expiresAt - now).TotalSeconds);

        return new PixWindowResult(seconds >= providerMinimum.TotalSeconds, expiresAt, Math.Max(0, seconds));
    }
}
