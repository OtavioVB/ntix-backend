namespace Domain.BoundedContexts.PaymentContext.Enums;

/// <summary>
/// Situação da integração do produtor. <see cref="INACTIVE"/> sai do ranqueamento, mas continua na rota e atendendo
/// webhooks e estornos dos pagamentos que já processou (RN11).
/// </summary>
public enum IntegrationStatus
{
    ACTIVE = 1,
    INACTIVE = 2,
}
