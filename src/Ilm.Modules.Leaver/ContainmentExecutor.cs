using Ilm.Application.Abstractions;
using Ilm.Application.Audit;
using Ilm.Application.Authority;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Okta;
using Ilm.Application.Protection;
using Ilm.Application.Provisioning;
using Ilm.Application.Sessions;
using Ilm.Application.Tasks;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Security;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.Leaver;

/// <summary>
/// Executes and verifies containment actions. Steps run in the mandated order (authentication, sessions, directory);
/// a failure in one step never stops later access-removing steps. Anything not automatable becomes a manual task
/// with a precise runbook, an alert and an SLA timer.
/// </summary>
public sealed class ContainmentExecutor(
    IIlmDbContext db,
    IActiveConfigurationProvider configuration,
    StrategyRegistry strategies,
    AuthorityService authority,
    ProtectionService protection,
    IDirectoryConnectorRegistry directories,
    IOktaLifecycleClient oktaLifecycle,
    IOktaUserClient oktaUsers,
    IEnumerable<ISessionConnector> sessionConnectors,
    ManualTaskService manualTasks,
    IAuditWriter audit,
    TimeProvider time)
{
    public const int MaxAutomaticAttempts = 3;

    public async Task ExecuteStepAsync(LeaverRequest request, ContainmentStep step, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = await configuration.GetAsync(cancellationToken);
        var policy = config.Document.LifecyclePolicies.SafelyContained;
        foreach (var action in request.Actions.Where(a => a.Step == step).OrderBy(a => a.Sequence))
        {
            if (action.Status is ContainmentActionStatus.Verified or ContainmentActionStatus.Skipped or ContainmentActionStatus.Succeeded
                || action.ManualTaskId is not null)
            {
                continue;
            }

            var started = time.GetUtcNow();
            action.AttemptedUtc = started.UtcDateTime;
            action.Attempts++;
            switch (action.Method)
            {
                case ContainmentMethod.ManualControlled:
                    await RequireManualAsync(request, action, action.Evidence ?? "Manual containment required.", actor, cancellationToken);
                    break;
                case ContainmentMethod.OktaApi:
                    await ContainOktaAsync(request, action, policy, actor, cancellationToken);
                    break;
                case ContainmentMethod.ContainmentOnlyLegacy:
                case ContainmentMethod.DirectActiveDirectory:
                    await ContainDirectoryAsync(request, action, actor, cancellationToken);
                    break;
                case ContainmentMethod.VerifyOnly:
                    await VerifyDirectoryAsync(action, cancellationToken);
                    break;
                case ContainmentMethod.SessionConnector:
                    await RevokeSessionsAsync(request, action, policy, actor, cancellationToken);
                    break;
                default:
                    await RequireManualAsync(request, action, $"Method {action.Method} is not executable automatically.", actor, cancellationToken);
                    break;
            }

            audit.Append(new AuditEvent
            {
                Action = "ContainmentActionExecuted",
                Result = action.Status.ToString(),
                OperationId = request.Id,
                CorrelationId = request.CorrelationId,
                IdempotencyKey = action.IdempotencyKey,
                TargetStableId = action.TargetStableId,
                Forest = action.ForestId,
                AuthorityDecision = action.AuthorityDecision,
                ProtectionDecision = action.ProtectionDecision,
                ConfigurationVersion = request.ConfigurationVersion,
                SelectedConnector = action.ConnectorId,
                SelectedDomainController = action.SelectedDomainController,
                AttemptedActions = new[] { $"{action.Step}:{action.Method}" },
                VerifiedActions = action.Status == ContainmentActionStatus.Verified ? new[] { action.Evidence } : Array.Empty<string?>(),
                WorkflowState = request.State.ToString(),
                DurationMs = (long)(time.GetUtcNow() - started).TotalMilliseconds,
                ExceptionCategory = action.ErrorCategory == SafeErrorCategory.None ? null : action.ErrorCategory.ToString(),
            }, actor);
        }
    }

    /// <summary>Re-reads every unverified action; closes manual tasks whose effect is now observed.</summary>
    public async Task VerifyAsync(LeaverRequest request, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = await configuration.GetAsync(cancellationToken);
        var policy = config.Document.LifecyclePolicies.SafelyContained;
        foreach (var action in request.Actions.Where(a => a.Status is not (ContainmentActionStatus.Verified or ContainmentActionStatus.Skipped)))
        {
            var before = action.Status;
            if (action.System == SystemKind.Okta && action.Step == ContainmentStep.AuthenticationContainment)
            {
                var user = await oktaUsers.GetUserAsync(action.TargetStableId, cancellationToken);
                if (user.Value?.Status is OktaUserStatus.Suspended or OktaUserStatus.Deprovisioned)
                {
                    MarkVerified(action, $"Okta status {user.Value.Status} (observed).");
                }
            }
            else if (action.System == SystemKind.ActiveDirectory && action.TargetIdentityId is not null && action.ConnectorId is not null)
            {
                await VerifyDirectoryAsync(action, cancellationToken);
                if (action.Method == ContainmentMethod.VerifyOnly && action.Status != ContainmentActionStatus.Verified && action.ManualTaskId is null
                    && action.AttemptedUtc is { } attempted && attempted.AddSeconds(policy.DirectoryVerificationTimeoutSeconds) <= time.GetUtcNow().UtcDateTime)
                {
                    var reason = policy.AllowEmergencyDirectoryOverride
                        ? "Okta did not disable the AD account within the verification window. The approved emergency override may be used with Security Approver approval, or disable manually."
                        : "Okta did not disable the AD account within the verification window; ILM must not write userAccountControl for Okta-owned accounts. Disable manually and investigate the Okta AD integration.";
                    await RequireManualAsync(request, action, reason, actor, cancellationToken);
                }
            }
            else if (action.Step == ContainmentStep.SessionRevocation && action.ManualTaskId is { } taskId)
            {
                var task = await db.ManualTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
                if (task?.Status is ManualTaskStatus.AwaitingVerification or ManualTaskStatus.Completed)
                {
                    MarkVerified(action, $"Operator recorded session termination: {task.CompletionEvidence}");
                }
            }

            if (action.Status == ContainmentActionStatus.Verified && before != ContainmentActionStatus.Verified && action.ManualTaskId is { } id)
            {
                var task = await db.ManualTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
                if (task is not null && task.Status != ManualTaskStatus.Completed)
                {
                    manualTasks.MarkVerified(task, actor);
                }
            }
        }
    }

    /// <summary>Final reconciliation: re-reads every contained account and confirms it is still contained.</summary>
    public async Task<bool> ConfirmStillContainedAsync(LeaverRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var action in request.Actions.Where(a => a.Step is ContainmentStep.AuthenticationContainment or ContainmentStep.DirectoryDisable && a.Status != ContainmentActionStatus.Skipped))
        {
            if (action.System == SystemKind.Okta)
            {
                var user = await oktaUsers.GetUserAsync(action.TargetStableId, cancellationToken);
                if (user.Value?.Status is not (OktaUserStatus.Suspended or OktaUserStatus.Deprovisioned))
                {
                    return false;
                }
            }
            else if (action.System == SystemKind.ActiveDirectory && action.TargetIdentityId is not null)
            {
                var identity = await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.Id == action.TargetIdentityId, cancellationToken);
                if (identity?.ConnectorId is null || identity.ObjectGuid is null)
                {
                    return false;
                }

                var reader = directories.GetReader(identity.ConnectorId);
                var dc = (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
                var state = await reader.ReadAccountStateAsync(identity.ObjectGuid.Value, dc, cancellationToken);
                if (state is not { IsDisabled: true })
                {
                    return false;
                }
            }
        }

        return true;
    }

    private async Task ContainOktaAsync(LeaverRequest request, ContainmentAction action, SafelyContainedPolicy policy, ActorContext actor, CancellationToken cancellationToken)
    {
        var state = await oktaLifecycle.GetLifecycleStateAsync(action.TargetStableId, cancellationToken);
        if (state.Value is OktaUserStatus.Deprovisioned || (state.Value is OktaUserStatus.Suspended && policy.OktaAction == OktaContainmentAction.Suspend))
        {
            action.Status = ContainmentActionStatus.Succeeded;
            action.Evidence = $"Okta user already {state.Value}.";
            return;
        }

        OktaResult result = OktaResult.Fail(0, SafeErrorCategory.Unexpected);
        for (var attempt = 1; attempt <= MaxAutomaticAttempts; attempt++)
        {
            result = policy.OktaAction == OktaContainmentAction.Suspend
                ? await oktaLifecycle.SuspendAsync(action.TargetStableId, cancellationToken)
                : await oktaLifecycle.DeactivateAsync(action.TargetStableId, cancellationToken);
            if (result.Succeeded || result.ErrorCategory is SafeErrorCategory.NotFound or SafeErrorCategory.NotAuthorised or SafeErrorCategory.ValidationFailed)
            {
                break;
            }
        }

        if (result.Succeeded)
        {
            action.Status = ContainmentActionStatus.Succeeded;
            action.ChangedExternalState = true;
            action.Evidence = $"Okta {policy.OktaAction} accepted (HTTP {result.StatusCode}).";
            return;
        }

        action.Status = ContainmentActionStatus.Failed;
        action.ErrorCategory = result.ErrorCategory;
        await RequireManualAsync(request, action, $"Okta {policy.OktaAction} failed after {MaxAutomaticAttempts} attempt(s) ({result.ErrorCategory}).", actor, cancellationToken);
    }

    private async Task ContainDirectoryAsync(LeaverRequest request, ContainmentAction action, ActorContext actor, CancellationToken cancellationToken)
    {
        var identity = await db.ExternalIdentities.AsNoTracking().FirstAsync(i => i.Id == action.TargetIdentityId, cancellationToken);
        var link = await db.IdentityLinks.AsNoTracking()
            .Where(l => l.TargetIdentityId == identity.Id && l.PersonId == request.PersonId && l.Confidence != LinkConfidence.Rejected)
            .OrderByDescending(l => l.Confidence == LinkConfidence.Authoritative).ThenByDescending(l => l.Confidence == LinkConfidence.HumanApproved)
            .FirstOrDefaultAsync(cancellationToken);
        var decision = await authority.ResolveAsync(identity, LifecycleAction.Leaver, AttributeSet.AccountEnabledState, cancellationToken);
        var protectionDecision = await protection.EvaluateAsync(identity.ConnectorId!, identity.ObjectGuid!.Value, cancellationToken);
        action.ProtectionDecision = protectionDecision.ToAuditString();
        action.AuthorityDecision = decision.ToAuditString();

        var kind = action.Method == ContainmentMethod.ContainmentOnlyLegacy ? StrategyKind.ContainmentOnlyLegacy : StrategyKind.DirectActiveDirectory;
        var strategy = strategies.Get(kind);
        var context = new ProvisioningContext
        {
            Action = LifecycleAction.Leaver,
            Identity = identity,
            LinkConfidence = link?.Confidence ?? LinkConfidence.Ambiguous,
            Authority = decision,
            Protection = protectionDecision,
            ApprovedLeaverRequest = true,
            OperationId = request.Id,
            IdempotencyKey = action.IdempotencyKey,
            DomainController = action.SelectedDomainController,
            Actor = actor,
        };
        var plan = await strategy.BuildPlanAsync(context, cancellationToken);
        var validation = await strategy.ValidatePlanAsync(context, plan, cancellationToken);
        if (!validation.IsValid)
        {
            action.ErrorCategory = SafeErrorCategory.ValidationFailed;
            await RequireManualAsync(request, action, "Automated containment is not permitted: " + string.Join(" ", validation.Errors), actor, cancellationToken);
            return;
        }

        ExecutionResult result = await strategy.ExecuteAsync(context, plan, cancellationToken);
        for (var attempt = 2; attempt <= MaxAutomaticAttempts && result.Outcome == ExecutionOutcome.FailedBeforeChange && result.ErrorCategory is SafeErrorCategory.ConnectorUnavailable or SafeErrorCategory.Timeout or SafeErrorCategory.ConcurrencyConflict; attempt++)
        {
            action.Attempts++;
            result = await strategy.ExecuteAsync(context, plan, cancellationToken);
        }

        action.SelectedDomainController = result.DomainController ?? action.SelectedDomainController;
        action.ChangedExternalState |= result.ChangedExternalState;
        if (result.Protection is not null)
        {
            action.ProtectionDecision = result.Protection.ToAuditString();
        }

        switch (result.Outcome)
        {
            case ExecutionOutcome.Succeeded:
            case ExecutionOutcome.AlreadyInDesiredState:
                MarkVerified(action, string.Join("; ", result.VerifiedSteps));
                break;
            case ExecutionOutcome.FailedAfterChange:
                action.Status = ContainmentActionStatus.Failed;
                action.ErrorCategory = result.ErrorCategory;
                await RequireManualAsync(request, action, $"Containment was attempted but not verified ({result.ErrorCategory}): {result.Detail}", actor, cancellationToken);
                break;
            default:
                action.Status = ContainmentActionStatus.Failed;
                action.ErrorCategory = result.ErrorCategory;
                await RequireManualAsync(request, action, $"Automated containment failed before any change ({result.ErrorCategory}): {result.Detail}", actor, cancellationToken);
                break;
        }
    }

    private async Task VerifyDirectoryAsync(ContainmentAction action, CancellationToken cancellationToken)
    {
        var identity = await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.Id == action.TargetIdentityId, cancellationToken);
        if (identity?.ConnectorId is null || identity.ObjectGuid is null)
        {
            return;
        }

        try
        {
            var reader = directories.GetReader(identity.ConnectorId);
            var dc = action.SelectedDomainController ?? (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
            action.SelectedDomainController = dc;
            var state = await reader.ReadAccountStateAsync(identity.ObjectGuid.Value, dc, cancellationToken);
            if (state is { IsDisabled: true })
            {
                MarkVerified(action, $"Disabled on {dc} (userAccountControl={state.UserAccountControl}, observed).");
            }
            else if (action.Status == ContainmentActionStatus.Pending)
            {
                action.Status = ContainmentActionStatus.InProgress;
                action.Evidence = $"Awaiting disable by {action.AuthorityDecision}; still enabled on {dc}.";
            }
        }
        catch (Exception ex) when (ex is IOException or KeyNotFoundException or TimeoutException)
        {
            action.ErrorCategory = SafeErrorCategory.ConnectorUnavailable;
            action.Evidence = $"Verification read failed ({ex.GetType().Name}).";
        }
    }

    private async Task RevokeSessionsAsync(LeaverRequest request, ContainmentAction action, SafelyContainedPolicy policy, ActorContext actor, CancellationToken cancellationToken)
    {
        var connector = sessionConnectors.FirstOrDefault(c => c.System == action.SessionSystem);
        if (connector is null)
        {
            action.Status = ContainmentActionStatus.NotConfigured;
            await RequireManualAsync(request, action, $"No {action.SessionSystem} session connector is registered.", actor, cancellationToken);
            return;
        }

        var identities = await db.ExternalIdentities.AsNoTracking().Where(i => i.PersonId == request.PersonId).ToListAsync(cancellationToken);
        var okta = identities.FirstOrDefault(i => i.System == SystemKind.Okta);
        var primary = identities.FirstOrDefault(i => i.System == SystemKind.ActiveDirectory && i.Population.StartsWith("target", StringComparison.OrdinalIgnoreCase))
            ?? identities.FirstOrDefault(i => i.System == SystemKind.ActiveDirectory);
        var result = await connector.RevokeAsync(new SessionRevocationRequest(
            request.PersonId,
            request.Id,
            action.IdempotencyKey,
            okta?.StableObjectId,
            identities.FirstOrDefault(i => i.EntraObjectId is not null)?.EntraObjectId,
            primary?.UserPrincipalName ?? okta?.UserPrincipalName,
            primary?.SamAccountName,
            policy.RevokeOktaOAuthTokens), cancellationToken);

        action.Evidence = result.Detail;
        switch (result.Status)
        {
            case SessionRevocationStatus.Succeeded:
                action.Status = result.VerifiedByReadBack ? ContainmentActionStatus.Verified : ContainmentActionStatus.Succeeded;
                action.ChangedExternalState = true;
                break;
            case SessionRevocationStatus.Failed:
                action.Status = ContainmentActionStatus.Failed;
                action.ErrorCategory = SafeErrorCategory.ExternalServiceError;
                await RequireManualAsync(request, action, $"{action.SessionSystem} session revocation failed: {result.Detail}", actor, cancellationToken);
                break;
            default:
                action.Status = result.Status switch
                {
                    SessionRevocationStatus.Unsupported => ContainmentActionStatus.Unsupported,
                    SessionRevocationStatus.NotConfigured => ContainmentActionStatus.NotConfigured,
                    SessionRevocationStatus.Unknown => ContainmentActionStatus.Unknown,
                    _ => ContainmentActionStatus.ManualActionRequired,
                };
                await RequireManualAsync(request, action, $"{action.SessionSystem}: {result.Detail} {connector.Coverage}", actor, cancellationToken);
                break;
        }
    }

    /// <summary>Turns an action into a manual task with a runbook, an alert and an SLA timer, without attempting it.</summary>
    public Task EscalateToManualAsync(LeaverRequest request, ContainmentAction action, string reason, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(action);
        return RequireManualAsync(request, action, reason, actor, cancellationToken);
    }

    private async Task RequireManualAsync(LeaverRequest request, ContainmentAction action, string reason, ActorContext actor, CancellationToken cancellationToken)
    {
        if (action.Status is not (ContainmentActionStatus.Failed or ContainmentActionStatus.NotConfigured or ContainmentActionStatus.Unsupported or ContainmentActionStatus.Unknown))
        {
            action.Status = ContainmentActionStatus.ManualActionRequired;
        }

        action.Evidence = reason;
        if (action.ManualTaskId is not null)
        {
            return;
        }

        var config = await configuration.GetAsync(cancellationToken);
        var policy = config.Document.LifecyclePolicies.SafelyContained;
        var tier0 = action.ProtectionDecision?.StartsWith("Protected", StringComparison.Ordinal) == true
            || action.ProtectionDecision?.StartsWith("Unknown", StringComparison.Ordinal) == true;
        var identity = action.TargetIdentityId is { } id ? await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken) : null;
        var person = await db.Persons.AsNoTracking().FirstAsync(p => p.Id == request.PersonId, cancellationToken);
        var sla = TimeSpan.FromMinutes(request.Urgency == LeaverUrgency.Urgent ? policy.UrgentContainmentSlaMinutes : policy.ManualContainmentSlaMinutes);
        var targets = identity is null
            ? new List<RunbookTarget> { new(action.System.ToString(), action.ConnectorId ?? "-", action.TargetStableId, null, null, action.TargetLabel) }
            : [RunbookGenerator.FromIdentity(identity)];
        var mandatoryBlocking = action.Mandatory;
        var task = manualTasks.Create(
            tier0 ? ManualTaskKind.Tier0Containment : action.Step == ContainmentStep.SessionRevocation ? ManualTaskKind.SessionRevocation : ManualTaskKind.ManualContainment,
            tier0 ? AlertSeverity.Critical : mandatoryBlocking ? AlertSeverity.High : AlertSeverity.Medium,
            $"{(mandatoryBlocking ? "Contain" : "Best-effort")}: {action.TargetLabel}",
            RunbookGenerator.ManualContainment(person.DisplayName, request.Id, reason, targets, tier0 ? "Tier 0 administrator on a PAW" : "Lifecycle Operator (Tier 1)", time.GetUtcNow().UtcDateTime.Add(sla), tier0),
            targets,
            sla,
            tier0 ? nameof(AppRole.SecurityApprover) : nameof(AppRole.LifecycleOperator),
            verificationRequired: action.System is SystemKind.ActiveDirectory or SystemKind.Okta,
            request.Id,
            action.Id,
            actor);
        action.ManualTaskId = task.Id;
    }

    private void MarkVerified(ContainmentAction action, string evidence)
    {
        action.Status = ContainmentActionStatus.Verified;
        action.VerifiedUtc = time.GetUtcNow().UtcDateTime;
        action.Evidence = evidence;
        action.ErrorCategory = SafeErrorCategory.None;
    }
}
