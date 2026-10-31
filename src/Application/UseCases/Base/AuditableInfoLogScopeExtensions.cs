using Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.Base;

public static class AuditableInfoLogScopeExtensions
{
    public static IDisposable? BeginAuditableScope(this ILogger logger, AuditableInfoValueObject auditableInfo)
    {
        if (!auditableInfo.IsValid)
            return null;

        return logger.BeginScope(new Dictionary<string, object?>
        {
            [nameof(AuditableInfoValueObject.CorrelationId)] = auditableInfo.CorrelationId,
            [nameof(AuditableInfoValueObject.OriginService)] = auditableInfo.OriginService,
            [nameof(AuditableInfoValueObject.ExecutionUser)] = auditableInfo.ExecutionUser,
            [nameof(AuditableInfoValueObject.RequestedAt)] = auditableInfo.RequestedAt,
        });
    }
}
