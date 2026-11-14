namespace Infra.PostgreSql.Repositories.Payments.Models;

/// <summary>Uma posição da rota: a integração que atende o método na prioridade informada.</summary>
public sealed record PaymentRouteModel(Guid Id, Guid ProducerId, string PaymentMethod, short Priority, Guid IntegrationId, DateTime UpdatedAt, Guid UpdatedCorrelationId);
