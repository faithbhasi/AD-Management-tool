using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Directory;
using Ilm.Application.Okta;
using Ilm.Application.Tasks;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Reconciliation;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Application.Reconciliation;

/// <summary>
/// Re-reads every known identity, records drift and out-of-band changes, and escalates any contained
/// account that has been re-enabled. Reconciliation never writes to a directory.
/// </summary>
public sealed class ReconciliationService(
    IIlmDbContext db,
    IDirectoryConnectorRegistry registry,
    IOktaUserClient oktaUsers,
    IOktaSystemLogClient oktaLog,
    AlertService alerts,
    IAuditWriter audit,
    TimeProvider time)
{
    public const string IlmOktaActorId = "ilm-service-app";

    public async Task<ReconciliationRun> RunAsync(string trigger, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var now = time.GetUtcNow().UtcDateTime;
        var lastRun = await db.ReconciliationRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Select(r => (DateTime?)r.StartedUtc).FirstOrDefaultAsync(cancellationToken);
        var run = new ReconciliationRun { StartedUtc = now, Trigger = trigger };
        db.ReconciliationRuns.Add(run);
        var findings = new List<ReconciliationFinding>();

        var containedIdentityIds = await ContainedIdentityIdsAsync(cancellationToken);
        var identities = await db.ExternalIdentities.ToListAsync(cancellationToken);
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var identity in identities)
        {
            run.IdentitiesChecked++;
            if (identity.System == SystemKind.ActiveDirectory && identity.ConnectorId is { } connectorId && identity.ObjectGuid is { } guid)
            {
                if (unavailable.Contains(connectorId))
                {
                    continue;
                }

                DirectoryObject? obj;
                try
                {
                    obj = await registry.GetReader(connectorId).GetByGuidAsync(guid, null, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unavailable.Add(connectorId);
                    findings.Add(Finding(run, null, ReconciliationFindingKind.ConnectorUnavailable, AlertSeverity.High, $"Connector '{connectorId}' unavailable ({ex.GetType().Name})."));
                    continue;
                }

                ReconcileDirectory(run, identity, obj, containedIdentityIds, findings, now);
            }
            else if (identity.System == SystemKind.Okta)
            {
                var result = await oktaUsers.GetUserAsync(identity.StableObjectId, cancellationToken);
                if (!result.Succeeded)
                {
                    findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.ConnectorUnavailable, AlertSeverity.Medium, "Okta user could not be read."));
                    continue;
                }

                var state = MapOkta(result.Value!.Status);
                if (containedIdentityIds.Contains(identity.Id) && state == IdentityState.Active)
                {
                    findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.ContainedAccountReEnabled, AlertSeverity.Critical, $"Contained Okta user {identity.StableObjectId} is ACTIVE again."));
                }
                else if (state != identity.State)
                {
                    findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.EnabledStateChanged, AlertSeverity.Low, $"Okta status now {result.Value.Status}."));
                }

                identity.State = state;
                identity.LastReconciledUtc = now;
            }
        }

        await DetectOutOfBandOktaChangesAsync(run, lastRun ?? now.AddHours(-1), findings, cancellationToken);

        run.FindingsCount = findings.Count(f => f.Kind != ReconciliationFindingKind.InSync);
        run.CompletedUtc = time.GetUtcNow().UtcDateTime;
        db.ReconciliationFindings.AddRange(findings);

        foreach (var critical in findings.Where(f => f.Severity >= AlertSeverity.High))
        {
            alerts.Raise(critical.Severity, "Reconciliation:" + critical.Kind, critical.Detail, null, actor);
        }

        await EscalateReEnabledLeaversAsync(findings, actor, cancellationToken);

        audit.Append(new AuditEvent
        {
            Action = "ReconciliationRun",
            Result = run.FindingsCount == 0 ? "InSync" : "DriftDetected",
            OperationId = run.Id,
            ReconciliationResults = new { run.IdentitiesChecked, run.FindingsCount, kinds = findings.GroupBy(f => f.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()) },
        }, actor);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private static void ReconcileDirectory(ReconciliationRun run, ExternalIdentity identity, DirectoryObject? obj, HashSet<Guid> containedIdentityIds, List<ReconciliationFinding> findings, DateTime now)
    {
        if (obj is null)
        {
            if (identity.State != IdentityState.NotFound)
            {
                findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.ObjectMissing, AlertSeverity.Medium, $"{identity.DisplayLabel} no longer found by objectGUID."));
            }

            identity.State = IdentityState.NotFound;
            identity.LastReconciledUtc = now;
            return;
        }

        if (!string.Equals(obj.DistinguishedName, identity.DistinguishedName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.DistinguishedNameChanged, AlertSeverity.Info, $"{identity.DisplayLabel} moved or renamed."));
            identity.DistinguishedName = obj.DistinguishedName;
        }

        var state = obj.IsDisabled ? IdentityState.Disabled : IdentityState.Active;
        if (containedIdentityIds.Contains(identity.Id) && state == IdentityState.Active)
        {
            findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.ContainedAccountReEnabled, AlertSeverity.Critical, $"Contained account {identity.DisplayLabel} is enabled again (out-of-band change)."));
        }
        else if (state != identity.State && identity.State != IdentityState.Unknown)
        {
            findings.Add(Finding(run, identity.Id, ReconciliationFindingKind.EnabledStateChanged, AlertSeverity.Low, $"{identity.DisplayLabel} enabled state changed to {state}."));
        }

        identity.State = state;
        identity.SamAccountName = obj.SamAccountName ?? identity.SamAccountName;
        identity.UserPrincipalName = obj.UserPrincipalName ?? identity.UserPrincipalName;
        identity.Mail = obj.Mail ?? identity.Mail;
        identity.LastReconciledUtc = now;
    }

    private async Task DetectOutOfBandOktaChangesAsync(ReconciliationRun run, DateTime sinceUtc, List<ReconciliationFinding> findings, CancellationToken cancellationToken)
    {
        var events = await oktaLog.QueryAsync(sinceUtc, null, "user.lifecycle.", cancellationToken);
        if (!events.Succeeded)
        {
            findings.Add(Finding(run, null, ReconciliationFindingKind.ConnectorUnavailable, AlertSeverity.Medium, "Okta System Log could not be read."));
            return;
        }

        foreach (var e in events.Value!.Where(e => !string.Equals(e.ActorId, IlmOktaActorId, StringComparison.Ordinal)))
        {
            var identity = await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.System == SystemKind.Okta && i.StableObjectId == e.TargetUserId, cancellationToken);
            findings.Add(Finding(run, identity?.Id, ReconciliationFindingKind.OutOfBandChange, AlertSeverity.High,
                $"Okta {e.EventType} for {e.TargetUserId} by {e.ActorType}:{e.ActorId} was not initiated by ILM."));
        }
    }

    private async Task<HashSet<Guid>> ContainedIdentityIdsAsync(CancellationToken cancellationToken)
    {
        LeaverState[] containedStates =
        [
            LeaverState.SafelyContained, LeaverState.RetentionActionsPending, LeaverState.OwnershipTransferPending,
            LeaverState.ReconciliationPending, LeaverState.ReconciliationRequired, LeaverState.Completed,
        ];
        var ids = await db.ContainmentActions.AsNoTracking()
            .Where(a => a.TargetIdentityId != null && (a.Step == ContainmentStep.DirectoryDisable || a.Step == ContainmentStep.AuthenticationContainment)
                && a.Status == ContainmentActionStatus.Verified)
            .Join(db.LeaverRequests.AsNoTracking().Where(r => containedStates.Contains(r.State)), a => a.LeaverRequestId, r => r.Id, (a, r) => a.TargetIdentityId!.Value)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private async Task EscalateReEnabledLeaversAsync(List<ReconciliationFinding> findings, ActorContext actor, CancellationToken cancellationToken)
    {
        var identityIds = findings.Where(f => f.Kind == ReconciliationFindingKind.ContainedAccountReEnabled && f.IdentityId is not null).Select(f => f.IdentityId!.Value).ToList();
        if (identityIds.Count == 0)
        {
            return;
        }

        var requestIds = await db.ContainmentActions.Where(a => a.TargetIdentityId != null && identityIds.Contains(a.TargetIdentityId.Value)).Select(a => a.LeaverRequestId).Distinct().ToListAsync(cancellationToken);
        var requests = await db.LeaverRequests.Where(r => requestIds.Contains(r.Id)).ToListAsync(cancellationToken);
        foreach (var request in requests.Where(r => LeaverStateMachine.CanTransition(r.State, LeaverState.ReconciliationRequired)))
        {
            var previous = request.State;
            request.State = LeaverState.ReconciliationRequired;
            request.IsActive = true;
            request.UpdatedUtc = time.GetUtcNow().UtcDateTime;
            db.LeaverTransitions.Add(new LeaverTransition
            {
                LeaverRequestId = request.Id,
                OperationId = request.Id,
                Ordinal = ++request.TransitionCount,
                CorrelationId = actor.CorrelationId,
                IdempotencyKey = request.IdempotencyKey,
                Actor = actor.Label,
                TimestampUtc = request.UpdatedUtc,
                Reason = "Reconciliation found a contained account re-enabled.",
                TicketReference = request.TicketReference,
                PreviousState = previous,
                NextState = LeaverState.ReconciliationRequired,
                ConfigurationVersion = request.ConfigurationVersion,
                SafeErrorCategory = SafeErrorCategory.VerificationFailed,
            });
        }
    }

    private static IdentityState MapOkta(string status) => status switch
    {
        OktaUserStatus.Suspended => IdentityState.Suspended,
        OktaUserStatus.Deprovisioned => IdentityState.Deprovisioned,
        OktaUserStatus.Staged => IdentityState.Staged,
        _ => IdentityState.Active,
    };

    private static ReconciliationFinding Finding(ReconciliationRun run, Guid? identityId, ReconciliationFindingKind kind, AlertSeverity severity, string detail) => new()
    {
        RunId = run.Id,
        IdentityId = identityId,
        Kind = kind,
        Severity = severity,
        Detail = detail,
        DetectedUtc = run.StartedUtc,
    };
}
