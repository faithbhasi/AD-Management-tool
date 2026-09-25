using Ilm.Application.Abstractions;
using Ilm.Application.Health;
using Ilm.Application.Provisioning;
using Ilm.Application.Security;
using Ilm.Domain.Approvals;
using Ilm.Domain.Audit;
using Ilm.Domain.Authority;
using Ilm.Domain.Leaver;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Web.Pages;

public sealed class IndexModel(IIlmDbContext db, IConnectorHealthSource health, StrategyRegistry strategies) : IlmPageModel
{
    public IReadOnlyList<ComponentHealth> Health { get; private set; } = [];

    public IReadOnlyList<Approval> PendingApprovals { get; private set; } = [];

    public IReadOnlyList<LeaverRequest> ActiveContainment { get; private set; } = [];

    public IReadOnlyList<ManualTask> ManualContainment { get; private set; } = [];

    public IReadOnlyList<LeaverTransition> RecentOperations { get; private set; } = [];

    public IReadOnlyList<Alert> OpenAlerts { get; private set; } = [];

    public IReadOnlyDictionary<StrategyKind, bool> Strategies { get; private set; } = new Dictionary<StrategyKind, bool>();

    public IReadOnlyList<Permission> MyPermissions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = Actor;
        MyPermissions = Enum.GetValues<Permission>().Where(p => Permissions.Has(actor, p)).ToList();
        Health = await health.CheckAsync(cancellationToken);
        Strategies = await strategies.GetEnabledStatesAsync(cancellationToken);
        PendingApprovals = (await db.Approvals.AsNoTracking().Where(a => a.Status == ApprovalStatus.Pending).OrderBy(a => a.CreatedUtc).Take(50).ToListAsync(cancellationToken))
            .Where(a => actor.HasRole(a.RequiredRole) || (a.RequiredRole == Domain.Security.AppRole.LifecycleApprover && actor.HasRole(Domain.Security.AppRole.SecurityApprover))).ToList();
        LeaverState[] inFlight =
        [
            LeaverState.ContainmentStarted, LeaverState.AuthenticationContained, LeaverState.SessionsRevoked, LeaverState.DirectoryAccountDisabled,
            LeaverState.ContainmentVerificationPending, LeaverState.ManualContainmentRequired, LeaverState.PartiallyContained,
            LeaverState.FailedBeforeChange, LeaverState.FailedAfterChange, LeaverState.ReconciliationRequired,
            LeaverState.Approved, LeaverState.Scheduled, LeaverState.AwaitingApproval,
        ];
        ActiveContainment = await db.LeaverRequests.AsNoTracking().Where(r => inFlight.Contains(r.State)).OrderByDescending(r => r.UpdatedUtc).Take(25).ToListAsync(cancellationToken);
        ManualContainment = await db.ManualTasks.AsNoTracking()
            .Where(t => (t.Kind == ManualTaskKind.ManualContainment || t.Kind == ManualTaskKind.Tier0Containment || t.Kind == ManualTaskKind.SessionRevocation || t.Kind == ManualTaskKind.IdentityLinkConfirmation)
                && t.Status != ManualTaskStatus.Completed && t.Status != ManualTaskStatus.Cancelled)
            .OrderBy(t => t.SlaDueUtc).Take(25).ToListAsync(cancellationToken);
        RecentOperations = await db.LeaverTransitions.AsNoTracking().OrderByDescending(t => t.TimestampUtc).Take(15).ToListAsync(cancellationToken);
        OpenAlerts = await db.Alerts.AsNoTracking().Where(a => a.AcknowledgedUtc == null).OrderByDescending(a => a.CreatedUtc).Take(10).ToListAsync(cancellationToken);
    }
}
