using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Approvals;

/// <summary>
/// Dual control. The requester can never approve their own request, the approver must hold the required role
/// (resolved without cache), and the approval binds to a content hash so material changes invalidate it.
/// </summary>
public sealed class ApprovalService(IIlmDbContext db, IAuditWriter audit, TimeProvider time)
{
    public Approval Create(
        ApprovalSubjectType subjectType,
        string subjectId,
        string summary,
        AppRole requiredRole,
        ActorContext requester,
        string contentHash,
        TimeSpan validity,
        long configurationVersion)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (requester.AppUserId is null)
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Approvals must be requested by a signed-in user.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var approval = new Approval
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            Summary = summary,
            RequiredRole = requiredRole,
            RequestedByUserId = requester.AppUserId.Value,
            RequestedByLabel = requester.Label,
            ContentHash = contentHash,
            CreatedUtc = now,
            ExpiresUtc = now.Add(validity),
            ConfigurationVersion = configurationVersion,
        };
        db.Approvals.Add(approval);
        audit.Append(new AuditEvent
        {
            Action = "ApprovalRequested",
            Result = "Pending",
            OperationId = approval.Id,
            TargetStableId = $"{subjectType}:{subjectId}",
            RequestedValues = new { subjectType, subjectId, requiredRole, contentHash, approval.ExpiresUtc },
            ConfigurationVersion = configurationVersion,
        }, requester);
        return approval;
    }

    /// <summary>Returns null when the actor may decide, otherwise the reason they may not.</summary>
    public string? DecisionBlocker(Approval approval, ActorContext actor, string currentContentHash)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(actor);

        if (approval.Status != ApprovalStatus.Pending)
        {
            return $"The approval is {approval.Status}.";
        }

        if (approval.ExpiresUtc <= time.GetUtcNow().UtcDateTime)
        {
            return "The approval has expired.";
        }

        if (actor.AppUserId is null || actor.AppUserId == approval.RequestedByUserId)
        {
            return "A requester cannot approve their own request.";
        }

        if (!actor.RolesFreshlyResolved)
        {
            return "Privileged roles must be re-verified before approving.";
        }

        var satisfied = actor.HasRole(approval.RequiredRole)
            || (approval.RequiredRole == AppRole.LifecycleApprover && actor.HasRole(AppRole.SecurityApprover));
        if (!satisfied)
        {
            return $"Approving requires the {approval.RequiredRole} role.";
        }

        if (!string.Equals(approval.ContentHash, currentContentHash, StringComparison.Ordinal))
        {
            return "The request changed after approval was requested; the approval is invalid.";
        }

        return null;
    }

    public async Task<Approval> DecideAsync(Guid approvalId, bool approve, string? comment, ActorContext actor, string currentContentHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Approval not found.");

        var now = time.GetUtcNow().UtcDateTime;
        if (approval.Status == ApprovalStatus.Pending && approval.ExpiresUtc <= now)
        {
            approval.Status = ApprovalStatus.Expired;
            Audit(approval, actor, "ApprovalExpired", "Expired");
            await db.SaveChangesAsync(cancellationToken);
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The approval has expired.");
        }

        if (approval.Status == ApprovalStatus.Pending && !string.Equals(approval.ContentHash, currentContentHash, StringComparison.Ordinal))
        {
            Invalidate(approval, "Material change after approval was requested.", actor);
            await db.SaveChangesAsync(cancellationToken);
            throw new DomainException(SafeErrorCategory.ApprovalInvalid, "The request changed after approval was requested; the approval is invalid.");
        }

        var blocker = DecisionBlocker(approval, actor, currentContentHash);
        if (blocker is not null)
        {
            Audit(approval, actor, "ApprovalDecisionDenied", "Denied", blocker);
            await db.SaveChangesAsync(cancellationToken);
            throw new DomainException(SafeErrorCategory.NotAuthorised, blocker);
        }

        approval.Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        approval.DecidedByUserId = actor.AppUserId;
        approval.DecidedByLabel = actor.Label;
        approval.DecidedUtc = now;
        approval.Comment = AuditSanitizer.SanitizeText(comment);
        approval.ConcurrencyStamp = Guid.NewGuid();
        Audit(approval, actor, approve ? "ApprovalGranted" : "ApprovalRejected", approval.Status.ToString());
        return approval;
    }

    public void Invalidate(Approval approval, string reason, ActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (approval.Status is ApprovalStatus.Pending or ApprovalStatus.Approved)
        {
            approval.Status = ApprovalStatus.Invalidated;
            approval.InvalidationReason = reason;
            approval.ConcurrencyStamp = Guid.NewGuid();
            Audit(approval, actor, "ApprovalInvalidated", "Invalidated", reason);
        }
    }

    public async Task<int> ExpireDueAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.Approvals.Where(a => a.Status == ApprovalStatus.Pending && a.ExpiresUtc <= now).ToListAsync(cancellationToken);
        foreach (var approval in due)
        {
            approval.Status = ApprovalStatus.Expired;
            Audit(approval, actor, "ApprovalExpired", "Expired");
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private void Audit(Approval approval, ActorContext actor, string action, string result, string? detail = null) =>
        audit.Append(new AuditEvent
        {
            Action = action,
            Result = result,
            OperationId = approval.Id,
            TargetStableId = $"{approval.SubjectType}:{approval.SubjectId}",
            Approval = $"{approval.Id}:{approval.Status}:{approval.RequiredRole}",
            RequestedValues = detail is null ? null : new { detail },
            ConfigurationVersion = approval.ConfigurationVersion,
        }, actor);
}
