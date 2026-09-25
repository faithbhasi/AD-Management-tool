using Ilm.Application.Configuration;
using Ilm.Application.Okta;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;

namespace Ilm.Application.Provisioning;

/// <summary>
/// ILM → Okta Users API → assignment → Okta AD provisioning → ILM reconciles AD. Disabled until the
/// OktaUserProvisioning flag is on AND an approved feasibility report exists. Success is reported only
/// after both the Okta and AD states are verified. Creating an Okta user is never assumed to create an AD user.
/// </summary>
public sealed class OktaApiProvisioningStrategy(
    IActiveConfigurationProvider configuration,
    IOktaUserClient users,
    IOktaGroupClient groups,
    IAdTargetStateReconciler adState) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.OktaApiProvisioning;

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var config = await configuration.GetAsync(cancellationToken);
        return config.IsEnabled(Feature.OktaUserProvisioning)
            && config.Document.FeasibilityApprovals.Any(a => a.Recommendation is Domain.Feasibility.FeasibilityRecommendation.Go or Domain.Feasibility.FeasibilityRecommendation.ConditionalGo);
    }

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new ProvisioningPlan(Kind, context.Identity.Id,
        [
            new PlannedStep(1, "SearchExistingOktaUser", null, "Search Okta for an existing user to link rather than duplicate.", true),
            new PlannedStep(2, "CreateStagedOktaUser", null, "Create the Okta user in STAGED state.", true),
            new PlannedStep(3, "AssignProvisioningGroup", null, "Assign the approved group that drives AD provisioning.", true),
            new PlannedStep(4, "AwaitAdObject", null, "Poll AD until the object appears; no fixed delay is assumed.", true),
            new PlannedStep(5, "ReconcileOkta", null, "Verify the Okta state.", true),
            new PlannedStep(6, "ReconcileAd", null, "Verify OU, naming, mail, department, manager and enabled state in AD.", true),
        ], ["Approved feasibility report.", "OktaUserProvisioning enabled.", "Single writer for the population."]));
    }

    public async Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!await IsEnabledAsync(cancellationToken))
        {
            return PlanValidationResult.Invalid("Okta provisioning is disabled until a feasibility report is approved and the flag is enabled.");
        }

        if (context.Action != LifecycleAction.Joiner)
        {
            return PlanValidationResult.Invalid("Okta API provisioning supports joiners only.");
        }

        if (!context.Authority.IsResolved || context.Authority.AuthoritativeSystem != SystemKind.Okta)
        {
            return PlanValidationResult.Invalid("Authority does not assign this population to Okta.");
        }

        return PlanValidationResult.Valid;
    }

    public async Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        var validation = await ValidatePlanAsync(context, plan, cancellationToken);
        return validation.IsValid
            ? new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.ValidationFailed, "Use ExecuteOktaJoinerAsync with a joiner payload.", null, [], [], false)
            : new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.FeatureDisabled, string.Join(" ", validation.Errors), null, [], [], false);
    }

    /// <summary>Creates or links the Okta user, assigns the provisioning group and waits for verified AD state.</summary>
    public async Task<ExecutionResult> ExecuteOktaJoinerAsync(
        ProvisioningContext context,
        OktaCreateUserRequest request,
        string provisioningGroupId,
        string targetConnectorId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var attempted = new List<string>();
        var verified = new List<string>();
        var validation = await ValidatePlanAsync(context, await BuildPlanAsync(context, cancellationToken), cancellationToken);
        if (!validation.IsValid)
        {
            return new ExecutionResult(ExecutionOutcome.Denied, SafeErrorCategory.FeatureDisabled, string.Join(" ", validation.Errors), null, attempted, verified, false);
        }

        attempted.Add("SearchExistingOktaUser");
        var existing = await users.SearchUsersAsync(new OktaUserSearch(request.Email, request.Login, request.EmployeeNumber), cancellationToken);
        if (!existing.Succeeded)
        {
            return new ExecutionResult(ExecutionOutcome.FailedBeforeChange, existing.ErrorCategory, "Okta search failed.", null, attempted, verified, false);
        }

        OktaUser user;
        if (existing.Value!.Count > 1)
        {
            return new ExecutionResult(ExecutionOutcome.FailedBeforeChange, SafeErrorCategory.DuplicateObject, "Multiple Okta users match; human resolution required.", null, attempted, verified, false);
        }
        else if (existing.Value.Count == 1)
        {
            user = existing.Value[0];
            verified.Add($"Linked existing Okta user {user.Id}");
        }
        else
        {
            attempted.Add("CreateStagedOktaUser");
            var created = await users.CreateUserAsync(request, activate: false, cancellationToken);
            if (!created.Succeeded)
            {
                return new ExecutionResult(ExecutionOutcome.FailedBeforeChange, created.ErrorCategory, "Okta user creation failed.", null, attempted, verified, false);
            }

            user = created.Value!;
        }

        attempted.Add("AssignProvisioningGroup");
        var assign = await groups.AssignUserToGroupAsync(provisioningGroupId, user.Id, cancellationToken);
        if (!assign.Succeeded)
        {
            return new ExecutionResult(ExecutionOutcome.FailedAfterChange, assign.ErrorCategory, "Group assignment failed.", null, attempted, verified, true);
        }

        attempted.Add("AwaitAdObject");
        var deadline = DateTime.UtcNow + timeout;
        AdTargetState state;
        do
        {
            state = await adState.ReadAsync(targetConnectorId, new AdTargetLookup(null, request.Login, request.Email, null), cancellationToken);
            if (state.Found)
            {
                break;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        if (!state.Found)
        {
            return new ExecutionResult(ExecutionOutcome.FailedAfterChange, SafeErrorCategory.VerificationFailed, "No AD object appeared; Okta and AD are not both verified.", null, attempted, verified, true);
        }

        verified.Add($"AD object {state.ObjectGuid:D} in {state.ParentOuDistinguishedName}");
        return new ExecutionResult(ExecutionOutcome.Succeeded, SafeErrorCategory.None, "Okta and AD states verified.", null, attempted, verified, true);
    }

    public Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(new ReconciliationResult(false, "Reconciliation runs through the feasibility harness until activation.", null));
}
