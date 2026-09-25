using Ilm.Application.Directory;
using Ilm.Application.Okta;
using Ilm.Domain.Authority;
using Ilm.Domain.Common;
using Ilm.Domain.Identity;

namespace Ilm.Application.Provisioning;

/// <summary>
/// The controlled fallback when automation is unavailable, not permitted, or authority is unresolved.
/// It never changes state itself; the caller creates the runbook task, alert and SLA timer. Reconcile
/// observes whether the manual action has taken effect.
/// </summary>
public sealed class ManualControlledStrategy(IDirectoryConnectorRegistry registry, IOktaUserClient oktaUsers) : IIdentityProvisioningStrategy
{
    public StrategyKind Kind => StrategyKind.ManualControlled;

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<ProvisioningPlan> BuildPlanAsync(ProvisioningContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new ProvisioningPlan(
            Kind,
            context.Identity.Id,
            [
                new PlannedStep(1, "GenerateRunbook", null, "Generate a precise runbook with exact targets and checks.", true),
                new PlannedStep(2, "RaiseAlert", null, "Raise a high-severity alert and start the SLA timer.", true),
                new PlannedStep(3, "AwaitManualAction", null, "An authorised operator performs the action outside ILM.", true),
                new PlannedStep(4, "VerifyObservedState", null, "ILM re-reads the target and closes the task only when the state is observed.", true),
            ],
            ["Automation unavailable, not permitted, or authority unresolved."]));
    }

    public Task<PlanValidationResult> ValidatePlanAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(PlanValidationResult.Valid);

    public Task<ExecutionResult> ExecuteAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(new ExecutionResult(ExecutionOutcome.ManualActionRequired, SafeErrorCategory.None, "Manual action required; runbook generated.", null, ["GenerateRunbook"], [], false));

    public async Task<ReconciliationResult> ReconcileAsync(ProvisioningContext context, ProvisioningPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = context.Identity;
        if (identity.System == SystemKind.ActiveDirectory && identity.ConnectorId is not null && identity.ObjectGuid is not null)
        {
            var reader = registry.GetReader(identity.ConnectorId);
            var dc = context.DomainController ?? (await reader.SelectWritableDomainControllerAsync(cancellationToken)).DomainController;
            var state = await reader.ReadAccountStateAsync(identity.ObjectGuid.Value, dc, cancellationToken);
            return state is null
                ? new ReconciliationResult(false, "Account not found.", dc)
                : new ReconciliationResult(state.IsDisabled, state.IsDisabled ? "Disabled (observed)." : "Still enabled.", dc);
        }

        if (identity.System == SystemKind.Okta)
        {
            var user = await oktaUsers.GetUserAsync(identity.StableObjectId, cancellationToken);
            var contained = user.Value?.Status is OktaUserStatus.Suspended or OktaUserStatus.Deprovisioned;
            return new ReconciliationResult(contained, user.Succeeded ? $"Okta status {user.Value?.Status}." : "Okta unavailable.", null);
        }

        return new ReconciliationResult(false, "No automated observation is available for this system.", null);
    }

    public static bool IsContainmentEligible(LinkConfidence confidence) => IdentityLinkPolicy.IsContainmentEligible(confidence);
}
