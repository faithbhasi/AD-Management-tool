using System.Text.RegularExpressions;
using Ilm.Application.Abstractions;
using Ilm.Application.Approvals;
using Ilm.Application.Audit;
using Ilm.Application.Configuration;
using Ilm.Application.Directory;
using Ilm.Application.Security;
using Ilm.Application.Tasks;
using Ilm.Domain.Approvals;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;
using Ilm.Domain.Leaver;
using Ilm.Domain.Security;
using Ilm.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Ilm.Modules.Leaver;

/// <summary>
/// The leaver workflow: request → validate → approve → (schedule) → contain in the mandated order → verify →
/// SafelyContained → non-urgent stages → reconcile → complete. A request can never silently disappear: every
/// failure path lands in a visible state with an alert, a manual task and an SLA.
/// </summary>
public sealed partial class LeaverWorkflowService(
    IIlmDbContext db,
    ContainmentPlanner planner,
    ContainmentExecutor executor,
    LeaverTransitions transitions,
    NonUrgentStageService nonUrgent,
    ApprovalService approvals,
    ManualTaskService manualTasks,
    ScopeEvaluator scopes,
    IDirectoryConnectorRegistry directories,
    IActiveConfigurationProvider configuration,
    IDistributedLock locks,
    IAuditWriter audit,
    TimeProvider time)
{
    public async Task<LeaverOperationResult> CreateAsync(CreateLeaverCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.RequestLeaver) || actor.AppUserId is null)
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Requesting a leaver requires the Lifecycle Operator role.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 128)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "An idempotency key (up to 128 characters) is required.");
        }

        var fingerprint = Hashing.Sha256Hex($"{command.PersonId}|{command.Reason.Trim()}|{command.TicketReference.Trim()}|{command.Urgency}|{command.EffectiveUtc:O}");
        var existing = await db.LeaverRequests.Include(r => r.Actions).FirstOrDefaultAsync(r => r.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal)
                ? new LeaverOperationResult(existing, "An identical request with this idempotency key already exists.", false)
                : throw new DomainException(SafeErrorCategory.IdempotencyConflict, "The idempotency key was already used for a different request.");
        }

        var person = await db.Persons.FirstOrDefaultAsync(p => p.Id == command.PersonId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Person not found.");
        if (!await IsInScopeAsync(actor, AppRole.LifecycleOperator, person.Id, cancellationToken))
        {
            // A leaver must never be blocked just because every account sits outside Tier 1 scopes (for example a
            // Tier 0-only person). Such a request is allowed only when nothing in the plan would be automated
            // against a directory or Okta; it then needs Security Approver approval and is contained manually.
            var preview = await planner.BuildAsync(person.Id, cancellationToken);
            if (preview.Actions.Any(a => !a.Skipped && a.Method is ContainmentMethod.OktaApi or ContainmentMethod.ContainmentOnlyLegacy or ContainmentMethod.DirectActiveDirectory or ContainmentMethod.VerifyOnly))
            {
                throw new DomainException(SafeErrorCategory.NotAuthorised, "None of the person's accounts could be confirmed within your OU scopes.");
            }
        }

        if (await db.LeaverRequests.AnyAsync(r => r.PersonId == person.Id && r.IsActive, cancellationToken))
        {
            throw new DomainException(SafeErrorCategory.ConcurrencyConflict, "An active leaver request already exists for this person.");
        }

        var config = await configuration.GetAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var request = new LeaverRequest
        {
            PersonId = person.Id,
            Reason = AuditSanitizer.SanitizeText(command.Reason.Trim()),
            TicketReference = command.TicketReference.Trim(),
            Urgency = command.Urgency,
            EffectiveUtc = command.EffectiveUtc ?? now,
            IdempotencyKey = command.IdempotencyKey,
            RequestFingerprint = fingerprint,
            RequestedByUserId = actor.AppUserId.Value,
            RequestedByLabel = actor.Label,
            CreatedUtc = now,
            UpdatedUtc = now,
            ConfigurationVersion = config.Version,
        };
        db.LeaverRequests.Add(request);
        audit.Append(new AuditEvent
        {
            Action = "LeaverRequested",
            Result = "Requested",
            OperationId = request.Id,
            CorrelationId = request.CorrelationId,
            IdempotencyKey = request.IdempotencyKey,
            TargetStableId = person.Id.ToString("D"),
            WorkflowState = LeaverState.Requested.ToString(),
            ConfigurationVersion = config.Version,
            RequestedValues = new { request.Urgency, request.EffectiveUtc, request.TicketReference, request.Reason },
        }, actor);

        var validationError = Validate(command, now);
        if (validationError is not null)
        {
            transitions.Move(request, LeaverState.ValidationFailed, validationError, actor, error: SafeErrorCategory.ValidationFailed);
            await SaveAsync(cancellationToken);
            return new LeaverOperationResult(request, validationError, true);
        }

        transitions.Move(request, LeaverState.Validated, "Request validated.", actor);
        person.LifecycleStatus = LifecycleStatus.LeaverRequested;
        await PlanAndRequestApprovalAsync(request, actor, cancellationToken);
        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, "Leaver requested; awaiting approval.", true);
    }

    public async Task<LeaverOperationResult> DecideAsync(Guid requestId, bool approve, string? comment, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var request = await LoadAsync(requestId, cancellationToken);
        if (request.State != LeaverState.AwaitingApproval || request.ApprovalId is null)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, $"The request is {request.State}, not awaiting approval.");
        }

        var plan = await planner.BuildAsync(request.PersonId, cancellationToken);
        if (!string.Equals(plan.Hash, request.PlanHash, StringComparison.Ordinal))
        {
            var approval = await db.Approvals.FirstAsync(a => a.Id == request.ApprovalId, cancellationToken);
            approvals.Invalidate(approval, "Material workflow-plan change.", actor);
            transitions.Move(request, LeaverState.Validated, "The containment plan changed; the previous approval is invalid.", actor, error: SafeErrorCategory.ApprovalInvalid);
            await PlanAndRequestApprovalAsync(request, actor, cancellationToken);
            await SaveAsync(cancellationToken);
            return new LeaverOperationResult(request, "The plan changed after approval was requested. Review the new plan and approve again.", true);
        }

        var decided = await approvals.DecideAsync(request.ApprovalId.Value, approve, comment, actor, request.PlanHash!, cancellationToken);
        if (decided.Status == ApprovalStatus.Rejected)
        {
            transitions.Move(request, LeaverState.Rejected, comment ?? "Rejected by approver.", actor, decided.DecidedByLabel);
            await RestorePersonStatusAsync(request, cancellationToken);
        }
        else
        {
            request.ApprovedByUserId = actor.AppUserId;
            transitions.Move(request, LeaverState.Approved, "Approved.", actor, decided.DecidedByLabel);
            if (request.EffectiveUtc > time.GetUtcNow().UtcDateTime.AddMinutes(1))
            {
                transitions.Move(request, LeaverState.Scheduled, $"Scheduled for {request.EffectiveUtc:u}.", actor, decided.DecidedByLabel);
            }
        }

        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, $"Request {request.State}.", true);
    }

    /// <summary>Executes containment for an approved (or due scheduled) request.</summary>
    public async Task<LeaverOperationResult> StartContainmentAsync(Guid requestId, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsSystem && (!Permissions.Has(actor, Permission.ExecuteContainment) || !actor.RolesFreshlyResolved))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Executing containment requires a freshly verified Lifecycle Operator role.");
        }

        var request = await LoadAsync(requestId, cancellationToken);
        if (request.State is not (LeaverState.Approved or LeaverState.Scheduled))
        {
            return new LeaverOperationResult(request, $"Containment cannot start from {request.State}.", false);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (request.State == LeaverState.Scheduled && request.EffectiveUtc > now)
        {
            return new LeaverOperationResult(request, "The request is scheduled for later.", false);
        }

        var config = await configuration.GetAsync(cancellationToken);
        var approval = await db.Approvals.FirstAsync(a => a.Id == request.ApprovalId, cancellationToken);
        var validity = TimeSpan.FromHours(config.Document.LifecyclePolicies.Approval.LeaverApprovalValidityHours);
        var executionDeadline = (request.Urgency == LeaverUrgency.Planned ? Max(request.EffectiveUtc, approval.DecidedUtc ?? now) : approval.DecidedUtc ?? now).Add(validity);
        if (approval.Status != ApprovalStatus.Approved || now > executionDeadline)
        {
            approvals.Invalidate(approval, "Approval expired before execution.", actor);
            transitions.Move(request, LeaverState.AwaitingApproval, "The approval expired before containment started; re-approval required.", actor, error: SafeErrorCategory.ApprovalInvalid);
            await RequestApprovalAsync(request, await planner.BuildAsync(request.PersonId, cancellationToken), actor, cancellationToken);
            await SaveAsync(cancellationToken);
            return new LeaverOperationResult(request, "The approval expired; a new approval has been requested.", true);
        }

        var plan = await planner.BuildAsync(request.PersonId, cancellationToken);
        if (!string.Equals(plan.Hash, request.PlanHash, StringComparison.Ordinal))
        {
            approvals.Invalidate(approval, "Material workflow-plan change before execution.", actor);
            transitions.Move(request, LeaverState.AwaitingApproval, "The containment plan changed after approval; re-approval required.", actor, error: SafeErrorCategory.ApprovalInvalid);
            await RequestApprovalAsync(request, plan, actor, cancellationToken);
            await SaveAsync(cancellationToken);
            return new LeaverOperationResult(request, "The plan changed after approval; a new approval has been requested.", true);
        }

        await using var handle = await locks.TryAcquireAsync($"leaver-person:{request.PersonId:D}", TimeSpan.FromMinutes(10), cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.ConcurrencyConflict, "Containment for this person is already running.");

        request.ContainmentStartedUtc = now;
        request.SlaDueUtc = now.AddMinutes(request.Urgency == LeaverUrgency.Urgent
            ? config.Document.LifecyclePolicies.SafelyContained.UrgentContainmentSlaMinutes
            : config.Document.LifecyclePolicies.SafelyContained.ManualContainmentSlaMinutes);
        MaterialiseActions(request, plan);
        transitions.Move(request, LeaverState.ContainmentStarted, "Containment started.", actor, approval.DecidedByLabel);
        await SaveAsync(cancellationToken);

        await RunContainmentAsync(request, plan, actor, cancellationToken);
        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, $"Containment finished in state {request.State}.", true);
    }

    /// <summary>Re-verifies an in-flight request (after manual work, Okta push delays, or out-of-band changes).</summary>
    public async Task<LeaverOperationResult> ReverifyAsync(Guid requestId, ActorContext actor, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(requestId, cancellationToken);
        if (request.State is not (LeaverState.ContainmentVerificationPending or LeaverState.ManualContainmentRequired or LeaverState.PartiallyContained or LeaverState.ReconciliationRequired))
        {
            return new LeaverOperationResult(request, $"Nothing to verify in state {request.State}.", false);
        }

        var plan = LeaverPlan.FromJson(request.PlanJson!);
        if (request.State == LeaverState.ReconciliationRequired)
        {
            transitions.Move(request, LeaverState.ManualContainmentRequired, "A contained account was found enabled; re-containment required.", actor, error: SafeErrorCategory.VerificationFailed);
            foreach (var a in request.Actions.Where(a => a.System is SystemKind.ActiveDirectory or SystemKind.Okta && a.Step != ContainmentStep.SessionRevocation))
            {
                a.Status = ContainmentActionStatus.Pending;
                a.ManualTaskId = null;
            }

            await executor.ExecuteStepAsync(request, ContainmentStep.AuthenticationContainment, actor, cancellationToken);
            await executor.ExecuteStepAsync(request, ContainmentStep.DirectoryDisable, actor, cancellationToken);
        }

        await executor.VerifyAsync(request, actor, cancellationToken);
        await EvaluateAsync(request, plan, actor, cancellationToken);
        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, $"Verification complete: {request.State}.", true);
    }

    public async Task<LeaverOperationResult> RecordManualTaskAsync(Guid taskId, string evidence, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.CompleteManualTask))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Recording manual task completion requires an operator or Security Approver role.");
        }

        var pending = await db.ManualTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Task not found.");
        if (pending.Kind == ManualTaskKind.Tier0Containment && !actor.HasRole(AppRole.SecurityApprover))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Tier 0 containment tasks are recorded by a Security Approver.");
        }

        var task = await manualTasks.RecordCompletionAsync(taskId, evidence, actor, cancellationToken);

        await SaveAsync(cancellationToken);
        if (task.LeaverRequestId is { } requestId)
        {
            var request = await LoadAsync(requestId, cancellationToken);
            if (LeaverStateMachine.IsContainmentInFlight(request.State) || request.State == LeaverState.ReconciliationRequired)
            {
                return await ReverifyAsync(requestId, actor, cancellationToken);
            }

            await AdvanceAsync(requestId, actor, cancellationToken);
            return new LeaverOperationResult(await LoadAsync(requestId, cancellationToken), "Task recorded.", true);
        }

        return new LeaverOperationResult(null!, "Task recorded.", true);
    }

    public async Task<LeaverOperationResult> CancelAsync(Guid requestId, string reason, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var request = await LoadAsync(requestId, cancellationToken);
        if (!Permissions.Has(actor, Permission.RequestLeaver) && !Permissions.Has(actor, Permission.ApproveLeaver))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Cancelling requires an operator or approver role.");
        }

        if (!LeaverStateMachine.IsCancellable(request.State))
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, $"A request in {request.State} cannot be cancelled; containment in progress must be rolled back through an approved rollback.");
        }

        if (request.ApprovalId is { } approvalId)
        {
            approvals.Invalidate(await db.Approvals.FirstAsync(a => a.Id == approvalId, cancellationToken), "Request cancelled.", actor);
        }

        transitions.Move(request, LeaverState.Cancelled, reason, actor);
        await RestorePersonStatusAsync(request, cancellationToken);
        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, "Cancelled.", true);
    }

    /// <summary>Requests rollback. Re-enabling access is a grant: it needs Security Approver approval and is performed manually.</summary>
    public async Task<LeaverOperationResult> RequestRollbackAsync(Guid requestId, string reason, ActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Permissions.Has(actor, Permission.RequestLeaver))
        {
            throw new DomainException(SafeErrorCategory.NotAuthorised, "Requesting rollback requires the Lifecycle Operator role.");
        }

        var request = await LoadAsync(requestId, cancellationToken);
        transitions.Move(request, LeaverState.RollbackRequested, reason, actor);
        var config = await configuration.GetAsync(cancellationToken);
        var approval = approvals.Create(ApprovalSubjectType.LeaverRequest, $"{request.Id:D}:rollback", $"Rollback (re-enable access) for leaver {request.Id:D}",
            AppRole.SecurityApprover, actor, Hashing.Sha256Hex($"rollback|{request.Id}|{request.PlanHash}"), TimeSpan.FromHours(24), config.Version);
        manualTasks.Create(ManualTaskKind.ManualContainment, AlertSeverity.High, "Rollback: restore access after Security Approver approval",
            $"Rollback of leaver `{request.Id:D}` was requested: {reason}.\n\nRe-enabling is an access grant. Do nothing until approval `{approval.Id:D}` is Approved. Then restore only the accounts listed in the containment plan, from the pre-containment state.",
            new[] { request.Id }, TimeSpan.FromHours(24), nameof(AppRole.LifecycleOperator), false, request.Id, null, actor);
        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, "Rollback requested; Security Approver approval and manual restoration required.", true);
    }

    /// <summary>Moves a SafelyContained request through the non-urgent stages to completion.</summary>
    public async Task<LeaverOperationResult> AdvanceAsync(Guid requestId, ActorContext actor, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(requestId, cancellationToken);
        var tasks = await db.ManualTasks.Where(t => t.LeaverRequestId == request.Id && t.Kind == ManualTaskKind.NonUrgentLeaverStage).ToListAsync(cancellationToken);
        bool Done(string prefix) => tasks.Where(t => t.Title.StartsWith(prefix, StringComparison.Ordinal)).All(t => t.Status is ManualTaskStatus.Completed or ManualTaskStatus.Cancelled);

        switch (request.State)
        {
            case LeaverState.SafelyContained:
                await nonUrgent.CreateTasksAsync(request, actor, cancellationToken);
                transitions.Move(request, LeaverState.RetentionActionsPending, "Non-urgent retention actions created.", actor);
                break;
            case LeaverState.RetentionActionsPending when Done(NonUrgentStageService.RetentionPrefix):
                transitions.Move(request, LeaverState.OwnershipTransferPending, "Retention actions complete.", actor);
                break;
            case LeaverState.OwnershipTransferPending when Done(NonUrgentStageService.OwnershipPrefix):
                transitions.Move(request, LeaverState.ReconciliationPending, "Ownership transfers complete.", actor);
                break;
            case LeaverState.ReconciliationPending:
                var stillContained = await executor.ConfirmStillContainedAsync(request, cancellationToken);
                if (stillContained)
                {
                    transitions.Move(request, LeaverState.Completed, "Final reconciliation confirmed contained state.", actor);
                    var person = await db.Persons.FirstAsync(p => p.Id == request.PersonId, cancellationToken);
                    person.LifecycleStatus = LifecycleStatus.Left;
                }
                else
                {
                    transitions.Move(request, LeaverState.ReconciliationRequired, "Final reconciliation found an account no longer contained.", actor, error: SafeErrorCategory.VerificationFailed);
                }

                break;
            case LeaverState.RollbackRequested:
                var rollbackApproval = await db.Approvals.FirstOrDefaultAsync(a => a.SubjectId == $"{request.Id:D}:rollback", cancellationToken);
                var rollbackTasksDone = await db.ManualTasks.Where(t => t.LeaverRequestId == request.Id && t.Title.StartsWith("Rollback", StringComparison.Ordinal)).AllAsync(t => t.Status == ManualTaskStatus.Completed, cancellationToken);
                if (rollbackApproval?.Status == ApprovalStatus.Approved && rollbackTasksDone)
                {
                    transitions.Move(request, LeaverState.Completed, "Rollback approved and completed manually.", actor, rollbackApproval.DecidedByLabel);
                }

                break;
            default:
                return new LeaverOperationResult(request, $"No automatic progression from {request.State}.", false);
        }

        await SaveAsync(cancellationToken);
        return new LeaverOperationResult(request, $"Now {request.State}.", true);
    }

    public async Task<LeaverRequest> LoadAsync(Guid requestId, CancellationToken cancellationToken) =>
        await db.LeaverRequests.Include(r => r.Actions).FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new DomainException(SafeErrorCategory.NotFound, "Leaver request not found.");

    private async Task RunContainmentAsync(LeaverRequest request, LeaverPlan plan, ActorContext actor, CancellationToken cancellationToken)
    {
        // 1. Authentication authority.
        await executor.ExecuteStepAsync(request, ContainmentStep.AuthenticationContainment, actor, cancellationToken);
        var authOk = StepSatisfied(request, ContainmentStep.AuthenticationContainment);
        if (authOk)
        {
            transitions.Move(request, LeaverState.AuthenticationContained, "Authentication authority contained.", actor);
        }

        // 2. Live sessions (always attempted, even if step 1 needs manual work).
        await executor.ExecuteStepAsync(request, ContainmentStep.SessionRevocation, actor, cancellationToken);
        var sessionsOk = authOk && StepSatisfied(request, ContainmentStep.SessionRevocation);
        if (sessionsOk)
        {
            transitions.Move(request, LeaverState.SessionsRevoked, "Supported live sessions revoked.", actor);
        }

        // 3. Authoritative directory account(s).
        await executor.ExecuteStepAsync(request, ContainmentStep.DirectoryDisable, actor, cancellationToken);
        var directoryOk = sessionsOk && StepSatisfied(request, ContainmentStep.DirectoryDisable, allowPendingVerification: true);
        if (directoryOk)
        {
            transitions.Move(request, LeaverState.DirectoryAccountDisabled, "Directory accounts disabled or awaiting owner verification.", actor);
            transitions.Move(request, LeaverState.ContainmentVerificationPending, "Verifying every mandatory control.", actor);
        }

        // 4. Verify, then decide.
        await executor.VerifyAsync(request, actor, cancellationToken);
        await EvaluateAsync(request, plan, actor, cancellationToken);
    }

    private async Task EvaluateAsync(LeaverRequest request, LeaverPlan plan, ActorContext actor, CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        await ContainLateConfirmedAccountsAsync(request, plan, actor, cancellationToken);
        var unresolvedLinks = await CountUnresolvedLinksAsync(request, plan, cancellationToken);
        var assessment = SafelyContainedEvaluator.Evaluate(config.Document.LifecyclePolicies.SafelyContained, request.Actions, unresolvedLinks);
        if (assessment.IsSafelyContained)
        {
            if (request.State != LeaverState.ContainmentVerificationPending)
            {
                transitions.Move(request, LeaverState.ContainmentVerificationPending, "All mandatory controls now observed; final verification.", actor);
            }

            request.SafelyContainedUtc = time.GetUtcNow().UtcDateTime;
            transitions.Move(request, LeaverState.SafelyContained, "SafelyContained: " + string.Join("; ", assessment.SatisfiedRequirements), actor);
            var person = await db.Persons.FirstAsync(p => p.Id == request.PersonId, cancellationToken);
            person.LifecycleStatus = LifecycleStatus.Contained;
            return;
        }

        var reason = "Not yet SafelyContained: " + string.Join("; ", assessment.UnmetRequirements);
        var anyFailedAfterChange = request.Actions.Any(a => a.Status == ContainmentActionStatus.Failed && a.ChangedExternalState);
        var anyManual = request.Actions.Any(a => a.ManualTaskId is not null && a.Status != ContainmentActionStatus.Verified);
        var anyChange = request.Actions.Any(a => a.ChangedExternalState || a.Status == ContainmentActionStatus.Verified);
        var pendingOnlyOwnerVerification = !anyManual && request.Actions.All(a => a.IsResolved || (a.Method == ContainmentMethod.VerifyOnly && a.Status == ContainmentActionStatus.InProgress)) && unresolvedLinks == 0;

        if (pendingOnlyOwnerVerification && request.State == LeaverState.ContainmentVerificationPending)
        {
            return;
        }

        LeaverState next;
        if (anyFailedAfterChange)
        {
            next = LeaverState.PartiallyContained;
        }
        else if (!anyChange && request.State == LeaverState.ContainmentStarted && request.Actions.Any(a => a.Status == ContainmentActionStatus.Failed))
        {
            transitions.Move(request, LeaverState.FailedBeforeChange, reason, actor, error: request.Actions.First(a => a.Status == ContainmentActionStatus.Failed).ErrorCategory);
            next = LeaverState.ManualContainmentRequired;
        }
        else
        {
            next = LeaverState.ManualContainmentRequired;
        }

        if (unresolvedLinks > 0)
        {
            await EnsureLinkConfirmationTasksAsync(request, plan, actor, cancellationToken);
        }

        if (request.State != next && LeaverStateMachine.CanTransition(request.State, next))
        {
            transitions.Move(request, next, reason, actor, error: SafeErrorCategory.VerificationFailed);
        }
    }

    private enum LinkResolution
    {
        Unconfirmed,
        Confirmed,
        Rejected,
    }

    /// <summary>Current state of the links to one account: any effective approved link confirms it; only rejected links reject it.</summary>
    private async Task<LinkResolution> ResolveLinksAsync(Guid identityId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var links = await db.IdentityLinks.AsNoTracking().Where(l => l.TargetIdentityId == identityId).ToListAsync(cancellationToken);
        if (links.Any(l => l.IsEffective(now) && IdentityLinkPolicy.IsContainmentEligible(l.Confidence)))
        {
            return LinkResolution.Confirmed;
        }

        return links.Count > 0 && links.All(l => l.Confidence == LinkConfidence.Rejected) ? LinkResolution.Rejected : LinkResolution.Unconfirmed;
    }

    /// <summary>
    /// Adds a manual containment action for each account whose link was confirmed after approval. The approved plan
    /// did not cover it, so ILM does not write to it automatically; the manual result is verified like any other.
    /// </summary>
    private async Task ContainLateConfirmedAccountsAsync(LeaverRequest request, LeaverPlan plan, ActorContext actor, CancellationToken cancellationToken)
    {
        foreach (var identityId in plan.UnresolvedLinks.Select(u => u.IdentityId).Distinct())
        {
            if (request.Actions.Any(a => a.TargetIdentityId == identityId))
            {
                continue;
            }

            var identity = await db.ExternalIdentities.AsNoTracking().FirstOrDefaultAsync(i => i.Id == identityId, cancellationToken);
            if (identity?.System is not (SystemKind.ActiveDirectory or SystemKind.Okta)
                || await ResolveLinksAsync(identityId, cancellationToken) != LinkResolution.Confirmed)
            {
                continue;
            }

            var planned = await planner.PlanLateConfirmedAsync(identity, cancellationToken);
            var action = ToAction(request, planned, request.Actions.Count == 0 ? 1 : request.Actions.Max(a => a.Sequence) + 1, "late");
            request.Actions.Add(action);
            db.ContainmentActions.Add(action);
            audit.Append(new AuditEvent
            {
                Action = "LeaverLateConfirmedAccountAdded",
                OperationId = request.Id,
                CorrelationId = request.CorrelationId,
                IdempotencyKey = action.IdempotencyKey,
                TargetStableId = planned.TargetStableId,
                ProtectionDecision = planned.ProtectionDecision,
                AuthorityDecision = planned.AuthorityDecision,
                WorkflowState = request.State.ToString(),
                Result = "ManualActionRequired",
            }, actor);
            await executor.EscalateToManualAsync(request, action, planned.ManualReason!, actor, cancellationToken);
        }
    }

    private async Task<int> CountUnresolvedLinksAsync(LeaverRequest request, LeaverPlan plan, CancellationToken cancellationToken)
    {
        var stillUnresolved = 0;
        foreach (var identityId in plan.UnresolvedLinks.Select(u => u.IdentityId).Distinct())
        {
            switch (await ResolveLinksAsync(identityId, cancellationToken))
            {
                case LinkResolution.Rejected:
                    continue;
                case LinkResolution.Unconfirmed:
                    stillUnresolved++;
                    continue;
            }

            // Newly confirmed: contained only once its action is verified. Accounts in systems the planner does not
            // contain directly (neither Okta nor AD) are covered by the session controls, as in the original plan.
            var system = await db.ExternalIdentities.AsNoTracking().Where(i => i.Id == identityId).Select(i => (SystemKind?)i.System).FirstOrDefaultAsync(cancellationToken);
            if (system is not (SystemKind.ActiveDirectory or SystemKind.Okta))
            {
                continue;
            }

            var action = request.Actions.FirstOrDefault(a => a.TargetIdentityId == identityId);
            if (action is null || action.Status != ContainmentActionStatus.Verified)
            {
                stillUnresolved++;
            }
        }

        return stillUnresolved;
    }

    private async Task EnsureLinkConfirmationTasksAsync(LeaverRequest request, LeaverPlan plan, ActorContext actor, CancellationToken cancellationToken)
    {
        foreach (var u in plan.UnresolvedLinks.DistinctBy(u => u.IdentityId))
        {
            if (await ResolveLinksAsync(u.IdentityId, cancellationToken) != LinkResolution.Unconfirmed)
            {
                continue;
            }

            var exists = await db.ManualTasks.AnyAsync(t => t.LeaverRequestId == request.Id && t.Kind == ManualTaskKind.IdentityLinkConfirmation && t.TargetsJson.Contains(u.IdentityId.ToString()), cancellationToken);
            if (exists)
            {
                continue;
            }

            var evidence = u.LinkId == Guid.Empty
                ? $"The account **{u.Label}** is associated with this leaver but has **no link record**."
                : $"The account **{u.Label}** is linked to this leaver only by **{u.Evidence}** evidence ({u.Confidence}).";
            manualTasks.Create(ManualTaskKind.IdentityLinkConfirmation, AlertSeverity.High, $"Confirm or reject link: {u.Label}",
                evidence + "\n\n" +
                "1. Establish whether it belongs to the leaver (ticket, HR, line manager).\n" +
                "2. If it does, approve the link (a second person). ILM then adds a manual containment task for the account and verifies it; if it does not, reject the link.\n" +
                "3. The leaver cannot be SafelyContained until this is resolved.",
                new { linkId = u.LinkId, identityId = u.IdentityId }, TimeSpan.FromHours(4), nameof(AppRole.LifecycleApprover), false, request.Id, null, actor);
        }
    }

    private async Task PlanAndRequestApprovalAsync(LeaverRequest request, ActorContext actor, CancellationToken cancellationToken)
    {
        var plan = await planner.BuildAsync(request.PersonId, cancellationToken);
        transitions.Move(request, LeaverState.AwaitingApproval, $"Plan with {plan.Actions.Count} control(s); requires {plan.RequiredApprovalRole}.", actor);
        await RequestApprovalAsync(request, plan, actor, cancellationToken);
    }

    private async Task RequestApprovalAsync(LeaverRequest request, LeaverPlan plan, ActorContext actor, CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        request.PlanJson = plan.ToJson();
        request.PlanHash = plan.Hash;
        request.ConfigurationVersion = config.Version;
        var requester = actor.IsSystem ? actor with { AppUserId = request.RequestedByUserId } : actor;
        var approval = approvals.Create(
            ApprovalSubjectType.LeaverRequest,
            request.Id.ToString("D"),
            $"Leaver for {plan.PersonLabel}: {plan.Actions.Count(a => !a.Skipped)} control(s), {plan.Actions.Count(a => a.Method == ContainmentMethod.ManualControlled)} manual, {plan.UnresolvedLinks.Count} unresolved link(s)",
            plan.RequiredApprovalRole,
            requester with { AppUserId = request.RequestedByUserId },
            plan.Hash,
            TimeSpan.FromHours(config.Document.LifecyclePolicies.Approval.LeaverApprovalValidityHours),
            config.Version);
        request.ApprovalId = approval.Id;
        await Task.CompletedTask;
    }

    private void MaterialiseActions(LeaverRequest request, LeaverPlan plan)
    {
        if (request.Actions.Count > 0)
        {
            return;
        }

        var sequence = 0;
        foreach (var p in plan.Actions)
        {
            var action = ToAction(request, p, ++sequence, null);
            request.Actions.Add(action);
            db.ContainmentActions.Add(action);
        }
    }

    private static ContainmentAction ToAction(LeaverRequest request, PlannedContainmentAction p, int sequence, string? keyPrefix) =>
            new()
            {
                LeaverRequestId = request.Id,
                Sequence = sequence,
                Step = p.Step,
                Method = p.Method,
                System = p.System,
                SessionSystem = p.SessionSystem,
                TargetIdentityId = p.TargetIdentityId,
                TargetStableId = p.TargetStableId,
                TargetLabel = p.TargetLabel,
                ConnectorId = p.ConnectorId,
                ForestId = p.ForestId,
                Mandatory = p.Mandatory,
                Status = p.Skipped ? ContainmentActionStatus.Skipped : ContainmentActionStatus.Pending,
                IdempotencyKey = keyPrefix is null ? $"{request.Id:N}:{p.Key}" : $"{request.Id:N}:{keyPrefix}:{p.Key}",
                AuthorityDecision = p.AuthorityDecision,
                ProtectionDecision = p.ProtectionDecision,
                Evidence = p.Skipped ? "Not applicable: the person has no identity in this system." : p.ManualReason,
            };

    private static bool StepSatisfied(LeaverRequest request, ContainmentStep step, bool allowPendingVerification = false) =>
        request.Actions.Where(a => a.Step == step && a.Mandatory).All(a =>
            a.Status is ContainmentActionStatus.Verified or ContainmentActionStatus.Succeeded or ContainmentActionStatus.Skipped
            || (allowPendingVerification && a.Method == ContainmentMethod.VerifyOnly && a.Status == ContainmentActionStatus.InProgress));

    /// <summary>The DN is resolved from the directory at request time (never trusted from the database).</summary>
    private async Task<bool> IsInScopeAsync(ActorContext actor, AppRole role, Guid personId, CancellationToken cancellationToken)
    {
        var directoryIdentities = await db.ExternalIdentities.AsNoTracking()
            .Where(i => i.PersonId == personId && i.System == SystemKind.ActiveDirectory && i.ConnectorId != null && i.ObjectGuid != null)
            .ToListAsync(cancellationToken);
        if (directoryIdentities.Count == 0)
        {
            return true;
        }

        foreach (var identity in directoryIdentities)
        {
            DirectoryObject? current;
            try
            {
                current = await directories.GetReader(identity.ConnectorId!).GetByGuidAsync(identity.ObjectGuid!.Value, null, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or KeyNotFoundException or TimeoutException)
            {
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var decision = await scopes.EvaluateAsync(actor, role, identity.ConnectorId!, current.DistinguishedName, cancellationToken);
            if (decision.InScope)
            {
                return true;
            }
        }

        return false;
    }

    private async Task RestorePersonStatusAsync(LeaverRequest request, CancellationToken cancellationToken)
    {
        var person = await db.Persons.FirstAsync(p => p.Id == request.PersonId, cancellationToken);
        if (person.LifecycleStatus == LifecycleStatus.LeaverRequested)
        {
            person.LifecycleStatus = LifecycleStatus.Active;
        }
    }

    private static string? Validate(CreateLeaverCommand command, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
        {
            return "A reason (up to 1000 characters) is required.";
        }

        if (!TicketPattern().IsMatch(command.TicketReference ?? string.Empty))
        {
            return "A ticket reference (letters, digits and dashes, 3 to 40 characters) is required.";
        }

        if (command.EffectiveUtc is { } effective && effective < now.AddDays(-30))
        {
            return "The effective date is more than 30 days in the past; confirm the date.";
        }

        return null;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new DomainException(SafeErrorCategory.ConcurrencyConflict, "The request conflicts with a concurrent change (for example another active leaver for this person).", ex);
        }
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{2,39}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TicketPattern();
}
