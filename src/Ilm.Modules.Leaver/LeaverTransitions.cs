using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Tasks;
using Ilm.Domain.Common;
using Ilm.Domain.Leaver;
using Ilm.Domain.Tasks;

namespace Ilm.Modules.Leaver;

/// <summary>
/// Records every durable leaver state change with the full evidence set required by the workflow specification,
/// writes the matching audit record, and raises an alert for any state that needs attention.
/// </summary>
public sealed class LeaverTransitions(IIlmDbContext db, IAuditWriter audit, AlertService alerts, TimeProvider time)
{
    public void Move(LeaverRequest request, LeaverState next, string reason, ActorContext actor, string? approver = null, SafeErrorCategory error = SafeErrorCategory.None)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        LeaverStateMachine.EnsureCanTransition(request.State, next);
        var now = time.GetUtcNow().UtcDateTime;
        var previous = request.State;
        var attempted = request.Actions.Where(a => a.Attempts > 0).Select(a => $"{a.Step}:{a.Method}:{a.TargetLabel}:{a.Status}").ToList();
        var verified = request.Actions.Where(a => a.Status is ContainmentActionStatus.Verified or ContainmentActionStatus.Succeeded).Select(a => $"{a.Step}:{a.TargetLabel}:{a.Status}").ToList();
        var unresolved = request.Actions.Where(a => !a.IsResolved).Select(a => $"{a.Step}:{a.TargetLabel}:{a.Status}").ToList();
        var authority = string.Join("; ", request.Actions.Select(a => a.AuthorityDecision).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal));
        var protection = string.Join("; ", request.Actions.Select(a => a.ProtectionDecision).Where(s => !string.IsNullOrEmpty(s) && s != "n/a").Distinct(StringComparer.Ordinal));

        var transition = new LeaverTransition
        {
            LeaverRequestId = request.Id,
            OperationId = request.Id,
            Ordinal = ++request.TransitionCount,
            CorrelationId = request.CorrelationId,
            IdempotencyKey = request.IdempotencyKey,
            Actor = actor.Label,
            Approver = approver,
            TimestampUtc = now,
            Reason = AuditSanitizer.SanitizeText(reason),
            TicketReference = request.TicketReference,
            PreviousState = previous,
            NextState = next,
            AuthorityDecision = authority,
            ProtectionDecision = protection,
            ConfigurationVersion = request.ConfigurationVersion,
            ActionsAttempted = string.Join(" | ", attempted),
            ActionsVerified = string.Join(" | ", verified),
            UnresolvedActions = string.Join(" | ", unresolved),
            SafeErrorCategory = error,
        };
        db.LeaverTransitions.Add(transition);

        request.State = next;
        request.UpdatedUtc = now;
        request.IsActive = !LeaverStateMachine.IsTerminal(next);
        request.ConcurrencyStamp = Guid.NewGuid();
        if (error != SafeErrorCategory.None)
        {
            request.LastErrorCategory = error;
        }

        audit.Append(new AuditEvent
        {
            Action = "LeaverStateChanged",
            Result = next.ToString(),
            OperationId = request.Id,
            CorrelationId = request.CorrelationId,
            IdempotencyKey = request.IdempotencyKey,
            TargetStableId = request.PersonId.ToString("D"),
            WorkflowState = next.ToString(),
            AuthorityDecision = authority,
            ProtectionDecision = protection,
            Approval = approver,
            ConfigurationVersion = request.ConfigurationVersion,
            BeforeValues = new { state = previous.ToString() },
            AppliedValues = new { state = next.ToString(), reason = transition.Reason, ticket = request.TicketReference },
            AttemptedActions = attempted,
            VerifiedActions = verified,
            ExceptionCategory = error == SafeErrorCategory.None ? null : error.ToString(),
        }, actor);

        if (LeaverStateMachine.RequiresAttention(next))
        {
            var severity = next is LeaverState.PartiallyContained or LeaverState.ManualContainmentRequired or LeaverState.FailedAfterChange or LeaverState.ReconciliationRequired
                ? AlertSeverity.Critical
                : AlertSeverity.High;
            alerts.Raise(severity, "Leaver:" + next, $"Leaver {request.Id:D} is {next}: {transition.Reason}", request.Id, actor);
        }
    }
}
