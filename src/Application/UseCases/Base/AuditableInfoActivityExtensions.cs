using Domain.ValueObjects;
using System.Diagnostics;

namespace Application.UseCases.Base;

public static class AuditableInfoActivityExtensions
{
    /// <summary>Abre o span do caso de uso com os campos auditáveis, quando válidos.</summary>
    public static Activity? StartAuditableActivity(this ActivitySource activitySource, string name, AuditableInfoValueObject auditableInfo)
    {
        var activity = activitySource.StartActivity(name);

        if (activity is not null && auditableInfo.IsValid)
        {
            activity.SetTag("ntix.correlation_id", auditableInfo.CorrelationId);
            activity.SetTag("ntix.origin_service", auditableInfo.OriginService);
            activity.SetTag("ntix.execution_user", auditableInfo.ExecutionUser);
            activity.SetTag("ntix.requested_at", auditableInfo.RequestedAt);
        }

        return activity;
    }
}
