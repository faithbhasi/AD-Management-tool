using Ilm.Application.Abstractions;
using Ilm.Domain.Audit;

namespace Ilm.Application.Audit;

public interface IAuditWriter
{
    /// <summary>Adds an audit record to the current unit of work. It is sealed into the chain when saved.</summary>
    AuditRecord Append(AuditEvent auditEvent, ActorContext actor);
}

public sealed class AuditWriter(IIlmDbContext db, TimeProvider time) : IAuditWriter
{
    public AuditRecord Append(AuditEvent auditEvent, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentNullException.ThrowIfNull(actor);

        var record = new AuditRecord
        {
            TimestampUtc = time.GetUtcNow().UtcDateTime,
            OperationId = auditEvent.OperationId,
            CorrelationId = auditEvent.CorrelationId ?? actor.CorrelationId,
            IdempotencyKey = AuditSanitizer.SanitizeText(auditEvent.IdempotencyKey).NullIfEmpty(),
            ActorIssuer = actor.Issuer,
            ActorSubject = actor.Subject,
            AppUserId = actor.AppUserId,
            EffectiveRoles = actor.EffectiveRolesText,
            Action = auditEvent.Action,
            TargetStableId = auditEvent.TargetStableId,
            Domain = auditEvent.Domain,
            Forest = auditEvent.Forest,
            OuGuid = auditEvent.OuGuid,
            AuthorityDecision = Clean(auditEvent.AuthorityDecision),
            ProtectionDecision = Clean(auditEvent.ProtectionDecision),
            ScopeDecision = Clean(auditEvent.ScopeDecision),
            Approval = Clean(auditEvent.Approval),
            ConfigurationVersion = auditEvent.ConfigurationVersion,
            BeforeValues = AuditSanitizer.ToSafeJson(auditEvent.BeforeValues),
            RequestedValues = AuditSanitizer.ToSafeJson(auditEvent.RequestedValues),
            AppliedValues = AuditSanitizer.ToSafeJson(auditEvent.AppliedValues),
            SelectedConnector = auditEvent.SelectedConnector,
            SelectedDomainController = auditEvent.SelectedDomainController,
            AttemptedActions = AuditSanitizer.ToSafeJson(auditEvent.AttemptedActions),
            VerifiedActions = AuditSanitizer.ToSafeJson(auditEvent.VerifiedActions),
            WorkflowState = auditEvent.WorkflowState,
            Result = auditEvent.Result,
            DurationMs = auditEvent.DurationMs,
            ExceptionCategory = auditEvent.ExceptionCategory,
            ReconciliationResults = AuditSanitizer.ToSafeJson(auditEvent.ReconciliationResults),
        };

        db.AuditRecords.Add(record);
        return record;
    }

    private static string? Clean(string? value) => value is null ? null : AuditSanitizer.SanitizeText(value);
}

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string value) => value.Length == 0 ? null : value;
}
