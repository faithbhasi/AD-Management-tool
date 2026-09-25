using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Common;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Tasks;

public sealed class ManualTaskService(IIlmDbContext db, IAuditWriter audit, AlertService alerts, TimeProvider time)
{
    public ManualTask Create(
        ManualTaskKind kind,
        AlertSeverity severity,
        string title,
        string runbookMarkdown,
        object targets,
        TimeSpan? sla,
        string assignedRole,
        bool verificationRequired,
        Guid? leaverRequestId,
        Guid? containmentActionId,
        ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var now = time.GetUtcNow().UtcDateTime;
        var task = new ManualTask
        {
            Kind = kind,
            Severity = severity,
            Title = title,
            RunbookMarkdown = runbookMarkdown,
            TargetsJson = AuditSanitizer.ToSafeJson(targets) ?? "[]",
            CreatedUtc = now,
            SlaDueUtc = sla is null ? null : now.Add(sla.Value),
            AssignedRole = assignedRole,
            VerificationRequired = verificationRequired,
            LeaverRequestId = leaverRequestId,
            ContainmentActionId = containmentActionId,
        };
        db.ManualTasks.Add(task);
        audit.Append(new AuditEvent
        {
            Action = "ManualTaskCreated",
            Result = task.Status.ToString(),
            OperationId = leaverRequestId,
            TargetStableId = task.Id.ToString("D"),
            RequestedValues = new { kind, severity, title, task.SlaDueUtc, assignedRole },
        }, actor);
        return task;
    }

    /// <summary>Records completion evidence. Tasks that need verification wait for ILM to observe the result.</summary>
    public async Task<ManualTask> RecordCompletionAsync(Guid taskId, string evidence, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var task = await db.ManualTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Task not found.");
        if (task.Status is ManualTaskStatus.Completed or ManualTaskStatus.Cancelled)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "The task is already closed.");
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "Completion evidence (for example a change ticket) is required.");
        }

        task.CompletionEvidence = AuditSanitizer.SanitizeText(evidence);
        task.CompletedByUserId = actor.AppUserId;
        task.Status = task.VerificationRequired ? ManualTaskStatus.AwaitingVerification : ManualTaskStatus.Completed;
        task.CompletedUtc = task.Status == ManualTaskStatus.Completed ? time.GetUtcNow().UtcDateTime : null;
        audit.Append(new AuditEvent
        {
            Action = "ManualTaskCompletionRecorded",
            Result = task.Status.ToString(),
            OperationId = task.LeaverRequestId,
            TargetStableId = task.Id.ToString("D"),
            RequestedValues = new { evidence = task.CompletionEvidence },
        }, actor);
        return task;
    }

    public void MarkVerified(ManualTask task, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.Status = ManualTaskStatus.Completed;
        task.CompletedUtc ??= time.GetUtcNow().UtcDateTime;
        audit.Append(new AuditEvent
        {
            Action = "ManualTaskVerified",
            Result = "Completed",
            OperationId = task.LeaverRequestId,
            TargetStableId = task.Id.ToString("D"),
        }, actor);
    }

    /// <summary>Raises escalation alerts for open tasks past their SLA.</summary>
    public async Task<int> EscalateBreachesAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var breached = await db.ManualTasks
            .Where(t => !t.SlaBreached && t.SlaDueUtc != null && t.SlaDueUtc <= now
                && (t.Status == ManualTaskStatus.Open || t.Status == ManualTaskStatus.InProgress || t.Status == ManualTaskStatus.AwaitingVerification))
            .ToListAsync(cancellationToken);
        foreach (var task in breached)
        {
            task.SlaBreached = true;
            alerts.Raise(AlertSeverity.Critical, "ManualTaskSlaBreached", $"Manual task '{task.Title}' breached its SLA.", task.LeaverRequestId, actor);
        }

        if (breached.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return breached.Count;
    }
}
