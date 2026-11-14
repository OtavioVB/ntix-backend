namespace Domain.BoundedContexts.PaymentContext.Enums;

/// <summary>Métodos de pagamento da Ntix (RN2). Método novo exige código, porque exige adaptador no fornecedor.</summary>
public enum PaymentMethod
{
    PIX = 1,
    BOLETO = 2,
    CREDIT_CARD = 3,
}
