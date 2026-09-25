using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Tasks;
using Ilm.Domain.Common;
using Ilm.Domain.Leaver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ilm.Modules.Leaver;

/// <summary>
/// Periodic work: expire stale approvals, start scheduled containment when due, re-verify in-flight cases,
/// advance post-containment stages and escalate SLA breaches.
/// </summary>
public sealed partial class LeaverMaintenanceService(
    IIlmDbContext db,
    LeaverWorkflowService workflow,
    ApprovalService approvals,
    ManualTaskService manualTasks,
    TimeProvider time,
    ILogger<LeaverMaintenanceService> logger)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var actor = ActorContext.System("leaver-scheduler");
        await approvals.ExpireDueAsync(actor, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;

        var due = await db.LeaverRequests.AsNoTracking().Where(r => r.State == LeaverState.Scheduled && r.EffectiveUtc <= now).Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var id in due)
        {
            await Guarded(id, () => workflow.StartContainmentAsync(id, actor, cancellationToken));
        }

        LeaverState[] verify = [LeaverState.ContainmentVerificationPending, LeaverState.ManualContainmentRequired, LeaverState.PartiallyContained, LeaverState.ReconciliationRequired];
        foreach (var id in await db.LeaverRequests.AsNoTracking().Where(r => verify.Contains(r.State)).Select(r => r.Id).ToListAsync(cancellationToken))
        {
            await Guarded(id, () => workflow.ReverifyAsync(id, actor, cancellationToken));
        }

        LeaverState[] advance = [LeaverState.SafelyContained, LeaverState.RetentionActionsPending, LeaverState.OwnershipTransferPending, LeaverState.ReconciliationPending, LeaverState.RollbackRequested];
        foreach (var id in await db.LeaverRequests.AsNoTracking().Where(r => advance.Contains(r.State)).Select(r => r.Id).ToListAsync(cancellationToken))
        {
            await Guarded(id, () => workflow.AdvanceAsync(id, actor, cancellationToken));
        }

        await manualTasks.EscalateBreachesAsync(actor, cancellationToken);
    }

    private async Task Guarded(Guid requestId, Func<Task<LeaverOperationResult>> work)
    {
        try
        {
            await work();
        }
        catch (DomainException ex)
        {
            LogMaintenanceFailure(logger, requestId, ex.Category);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leaver maintenance for {RequestId} failed: {Category}")]
    private static partial void LogMaintenanceFailure(ILogger logger, Guid requestId, SafeErrorCategory category);
}
