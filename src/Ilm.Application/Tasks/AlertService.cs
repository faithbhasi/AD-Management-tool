using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Tasks;
using Microsoft.Extensions.Logging;

namespace Ilm.Application.Tasks;

public sealed partial class AlertService(IIlmDbContext db, IAuditWriter audit, TimeProvider time, ILogger<AlertService> logger)
{
    public Alert Raise(AlertSeverity severity, string category, string message, Guid? operationId, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var alert = new Alert
        {
            Severity = severity,
            Category = category,
            Message = AuditSanitizer.SanitizeText(message),
            OperationId = operationId,
            CorrelationId = actor.CorrelationId,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Alerts.Add(alert);
        audit.Append(new AuditEvent
        {
            Action = "AlertRaised",
            Result = severity.ToString(),
            OperationId = operationId,
            RequestedValues = new { category, severity },
        }, actor);
        LogAlert(logger, severity, category, operationId);
        return alert;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ILM alert {Severity} {Category} for operation {OperationId}")]
    private static partial void LogAlert(ILogger logger, AlertSeverity severity, string category, Guid? operationId);
}
